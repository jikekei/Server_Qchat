using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using PlayerRoles;
using UnityEngine;
#if LABAPI
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using Player = LabApi.Features.Wrappers.Player;
using PlayerEvents = LabApi.Events.Handlers.PlayerEvents;
using ServerEvents = LabApi.Events.Handlers.ServerEvents;
#else
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using Player = Exiled.API.Features.Player;
using PlayerEvents = Exiled.Events.Handlers.Player;
using ServerEvents = Exiled.Events.Handlers.Server;
#endif

namespace Qchat.GameDatabase
{
    internal sealed class GameDatabaseRuntime : MonoBehaviour
    {
        private sealed class Session
        {
            public string Id;
            public string Name;
            public bool TrackStats;
            public bool Kicked;
            public long Checkpoint;
        }

        private sealed class SyncResult
        {
            public Dictionary<string, string> Bans;
            public Dictionary<string, PlayerDelta> Retry;
            public string BanError;
            public string WriteError;
        }

        private static GameDatabaseRuntime instance;
        private readonly Dictionary<Player, Session> sessions = new Dictionary<Player, Session>();
        private Dictionary<string, PlayerDelta> pending = new Dictionary<string, PlayerDelta>();
        private GameDatabaseStore database;
        private Task<SyncResult> sync;
        private long nextPoll;
        private long nextCheckpoint;
        private bool trackPlayerStats;
        private bool stopped;
        private string lastBanError;
        private string lastWriteError;

        public static void StartRuntime(string connectionString, bool trackPlayerStats)
        {
            if (instance != null || string.IsNullOrWhiteSpace(connectionString))
                return;

            GameDatabaseStore store;
            try { store = new GameDatabaseStore(connectionString); }
            catch (ArgumentException)
            {
                UnityEngine.Debug.LogError("[Qcha 数据库] 连接字符串格式无效，请检查插件配置。");
                return;
            }

            var host = new GameObject("Qcha.GameDatabase");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<GameDatabaseRuntime>();
            instance.database = store;
            instance.trackPlayerStats = trackPlayerStats;
            instance.Subscribe();
        }

        public static void StopRuntime()
        {
            if (instance == null)
                return;

            instance.FlushOnStop();
            Destroy(instance.gameObject);
            instance = null;
        }

        private void Subscribe()
        {
#if LABAPI
            PlayerEvents.Joined += OnJoined;
            PlayerEvents.Left += OnLeft;
            PlayerEvents.Death += OnDeath;
            foreach (var player in Player.ReadyList) Track(player);
#else
            PlayerEvents.Verified += OnJoined;
            PlayerEvents.Left += OnLeft;
            PlayerEvents.Died += OnDeath;
            foreach (var player in Player.List) Track(player);
#endif
            ServerEvents.RoundEnded += OnRoundEnded;
        }

#if LABAPI
        private static bool IsConnected(Player player) => player != null && !player.IsDestroyed && player.IsPlayer && player.IsReady;
        private void OnJoined(PlayerJoinedEventArgs ev) => Track(ev.Player);
        private void OnLeft(PlayerLeftEventArgs ev) => Leave(ev.Player);
        private void OnDeath(PlayerDeathEventArgs ev) => RecordDeath(ev.Player, ev.Attacker, ev.OldRole.GetTeam() == Team.SCPs);
#else
        private static bool IsConnected(Player player) => player != null && !player.IsHost && !player.IsNPC && player.IsConnected && player.IsVerified;
        private void OnJoined(VerifiedEventArgs ev) => Track(ev.Player);
        private void OnLeft(LeftEventArgs ev) => Leave(ev.Player);
        private void OnDeath(DiedEventArgs ev) => RecordDeath(ev.Player, ev.Attacker, ev.TargetOldRole.GetTeam() == Team.SCPs);
#endif

        private void Track(Player player)
        {
            if (!IsConnected(player) || string.IsNullOrWhiteSpace(player.UserId) || sessions.ContainsKey(player))
                return;

            var session = new Session
            {
                Id = player.UserId,
                Name = player.Nickname ?? "",
                TrackStats = trackPlayerStats && !player.DoNotTrack,
                Checkpoint = Stopwatch.GetTimestamp(),
            };
            sessions.Add(player, session);
            if (session.TrackStats)
                Delta(session);

            nextPoll = 0;
        }

        private void Leave(Player player)
        {
            if (player == null || !sessions.TryGetValue(player, out var session))
                return;
            AccrueTime(session, Stopwatch.GetTimestamp());
            sessions.Remove(player);
        }

        private void RecordDeath(Player victim, Player attacker, bool victimWasScp)
        {
            if (victim == null || !sessions.TryGetValue(victim, out var dead))
                return;

            if (dead.TrackStats)
                Delta(dead).Deaths++;

            if (attacker == null || !sessions.TryGetValue(attacker, out var killer) ||
                !killer.TrackStats || killer.Id == dead.Id)
                return;

            if (victimWasScp)
                Delta(killer).ScpKills++;
            else
                Delta(killer).PlayerKills++;
        }

        private PlayerDelta Delta(Session session)
        {
            if (!pending.TryGetValue(session.Id, out var delta))
            {
                delta = new PlayerDelta();
                pending.Add(session.Id, delta);
            }
            delta.Name = session.Name;
            return delta;
        }

        private void AccrueTime(Session session, long now)
        {
            if (!session.TrackStats)
                return;

            int seconds = (int)((now - session.Checkpoint) / Stopwatch.Frequency);
            if (seconds <= 0)
                return;

            session.Checkpoint += seconds * Stopwatch.Frequency;
            Delta(session).PlayTime += seconds;
        }

        private void OnRoundEnded(RoundEndedEventArgs ev)
        {
            nextCheckpoint = 0;
            nextPoll = 0;
        }

        private void Update()
        {
            if (stopped)
                return;

            if (sync != null)
            {
                if (!sync.IsCompleted)
                    return;
                Apply(sync.GetAwaiter().GetResult());
                sync = null;
            }

            long now = Stopwatch.GetTimestamp();
            if (now < nextPoll)
                return;
            nextPoll = now + 5 * Stopwatch.Frequency;

            if (now >= nextCheckpoint)
            {
                foreach (var entry in sessions)
                {
                    if (IsConnected(entry.Key))
                    {
                        entry.Value.Name = entry.Key.Nickname ?? "";
                        AccrueTime(entry.Value, now);
                    }
                }
                nextCheckpoint = now + 30 * Stopwatch.Frequency;
            }

            var ids = sessions.Values.Where(player => !player.Kicked).Select(player => player.Id).Distinct().ToArray();
            if (ids.Length > 0 || pending.Count > 0)
            {
                var batch = pending;
                pending = new Dictionary<string, PlayerDelta>();
                sync = Task.Run(() => SyncAsync(batch, ids));
            }
        }

        private async Task<SyncResult> SyncAsync(Dictionary<string, PlayerDelta> changes, string[] ids)
        {
            var result = new SyncResult();
            try { result.Bans = await database.GetActiveBansAsync(ids).ConfigureAwait(false); }
            catch (Exception ex) { result.BanError = ex.Message; }

            if (changes.Count == 0)
                return result;

            bool committing = false;
            try { await database.SavePlayersAsync(changes, () => committing = true).ConfigureAwait(false); }
            catch (Exception ex)
            {
                if (!committing) result.Retry = changes;
                result.WriteError = committing ? "提交结果不明：" + ex.Message : ex.Message;
            }
            return result;
        }

        private void Apply(SyncResult result)
        {
            Merge(pending, result.Retry);
            Report("查询封禁", ref lastBanError, result.BanError);
            Report("写入玩家统计", ref lastWriteError, result.WriteError);
            if (result.Bans == null || result.Bans.Count == 0)
                return;

            // Kick can raise Left synchronously, so enumerate a snapshot.
            foreach (var entry in sessions.ToArray())
            {
                var session = entry.Value;
                if (!session.Kicked && IsConnected(entry.Key) && result.Bans.TryGetValue(session.Id, out var reason))
                {
                    session.Kicked = true;
                    entry.Key.Kick(string.IsNullOrWhiteSpace(reason) ? "数据库封禁" : "数据库封禁：" + reason);
                }
            }
        }

        private static void Merge(Dictionary<string, PlayerDelta> target, Dictionary<string, PlayerDelta> older)
        {
            if (older == null) return;
            foreach (var entry in older)
            {
                if (target.TryGetValue(entry.Key, out var current)) current.Add(entry.Value);
                else target.Add(entry.Key, entry.Value);
            }
        }

        private static void Report(string operation, ref string previous, string error)
        {
            if (error != null && error != previous)
                UnityEngine.Debug.LogWarning("[Qcha 数据库] " + operation + "失败：" + error);
            previous = error;
        }

        private void OnApplicationQuit() => FlushOnStop();

        private void FlushOnStop()
        {
            if (stopped)
                return;
            stopped = true;
#if LABAPI
            PlayerEvents.Joined -= OnJoined;
            PlayerEvents.Left -= OnLeft;
            PlayerEvents.Death -= OnDeath;
#else
            PlayerEvents.Verified -= OnJoined;
            PlayerEvents.Left -= OnLeft;
            PlayerEvents.Died -= OnDeath;
#endif
            ServerEvents.RoundEnded -= OnRoundEnded;
            long now = Stopwatch.GetTimestamp();
            foreach (var session in sessions.Values) AccrueTime(session, now);
            var previous = sync;
            var batch = pending;
            var flush = Task.Run(async () =>
            {
                if (previous != null) Merge(batch, (await previous.ConfigureAwait(false)).Retry);
                return await SyncAsync(batch, Array.Empty<string>()).ConfigureAwait(false);
            });

            if (!flush.Wait(2500))
                UnityEngine.Debug.LogWarning("[Qcha 数据库] 关闭时尚有玩家统计等待保存。");
            else
                Report("关闭时写入玩家统计", ref lastWriteError, flush.Result.WriteError);
        }
    }
}
