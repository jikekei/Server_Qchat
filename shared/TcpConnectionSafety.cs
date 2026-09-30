#if NET8_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Qchat.GameAdmin
{
    /// <summary>Bounds resource use before authentication, including clients with incomplete headers.</summary>
    public sealed class TcpConnectionLimit : IDisposable
    {
        private readonly int maximum;
        private readonly object gate = new object();
        private readonly HashSet<TcpClient> clients = new HashSet<TcpClient>();
        private bool stopped;

        public TcpConnectionLimit(int maximum)
        {
            if (maximum < 1) throw new ArgumentOutOfRangeException(nameof(maximum));
            this.maximum = maximum;
        }

        public bool TryAdd(TcpClient client)
        {
            lock (gate)
            {
                if (stopped || clients.Count >= maximum) return false;
                return clients.Add(client);
            }
        }

        public void Remove(TcpClient client) { lock (gate) clients.Remove(client); }

        public void Dispose()
        {
            TcpClient[] remaining;
            lock (gate)
            {
                stopped = true;
                remaining = new TcpClient[clients.Count];
                clients.CopyTo(remaining);
                clients.Clear();
            }
            foreach (var client in remaining) { try { client.Close(); } catch { } }
        }
    }

    public static class TcpDeadline
    {
        // Mono may ignore cancellation after an asynchronous socket read has started.
        // Race the entire operation against a deadline, close its socket, and never wait for a stuck read.
        public static async Task<T> Run<T>(Func<Task<T>> operation, Action close, int timeoutMs, CancellationToken ct)
        {
            if (timeoutMs < 1) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            ct.ThrowIfCancellationRequested();
            using (var timer = CancellationTokenSource.CreateLinkedTokenSource(ct))
            using (ct.Register(() => Abort(close)))
            {
                var pending = operation();
                var delay = Task.Delay(timeoutMs, timer.Token);
                if (await Task.WhenAny(pending, delay).ConfigureAwait(false) != pending)
                {
                    Abort(close);
                    // A close should unblock Mono, but observe a later failure without relying on that behavior.
                    Observe(pending);
                    ct.ThrowIfCancellationRequested();
                    throw new TimeoutException("TCP operation exceeded its deadline");
                }
                timer.Cancel();
                return await pending.ConfigureAwait(false);
            }
        }

        public static Task<bool> Run(Func<Task> operation, Action close, int timeoutMs, CancellationToken ct)
        {
            return Run(async () => { await operation().ConfigureAwait(false); return true; }, close, timeoutMs, ct);
        }

        private static void Abort(Action close) { try { close(); } catch { } }
        private static void Observe(Task task)
        {
            task.ContinueWith(t => { var ignored = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
