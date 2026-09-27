using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Qchat.Security
{
    /// <summary>
    /// 插件与机器人之间的 TCP 鉴权封套。
    /// 报文格式：v2|unix秒|nonce|base64(hmac)|负载。
    /// HMAC-SHA256 的密钥是 Token 的 UTF-8，原文是「unix秒\nnonce\n负载」。
    /// 这条链路只鉴权、不加密；跨机器部署请走 VPN 或防火墙。
    /// 本文件同时被 EXILED 插件、LabAPI 插件和机器人编译，请保持 C# 7 语法。
    /// </summary>
    public static class TcpAuthEnvelope
    {
        public const string Version = "v2";
        public const string RetiredDefaultToken = "QchaSecret_123";
        public const int AllowedSkewSeconds = 120;

        public static bool IsRejectedToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return true;
            return string.Equals(token.Trim(), RetiredDefaultToken, StringComparison.Ordinal);
        }

        public static bool TrySeal(string token, string payload, out string wire, out string error)
        {
            return TrySeal(token, payload, DateTimeOffset.UtcNow, "", out wire, out error);
        }

        public static bool TrySeal(string token, string payload, DateTimeOffset utcNow, string nonce, out string wire, out string error)
        {
            wire = "";
            error = "";
            if (IsRejectedToken(token))
            {
                error = "Token 为空或仍是已公开的旧默认值";
                return false;
            }

            if (string.IsNullOrEmpty(payload))
                payload = "";
            if (string.IsNullOrEmpty(nonce))
                nonce = CreateNonce();
            if (!IsValidNonce(nonce))
            {
                error = "nonce 不合法";
                return false;
            }

            long unix = utcNow.ToUnixTimeSeconds();
            string mac = ComputeMac(token.Trim(), unix, nonce, payload);
            wire = Version + "|" + unix.ToString() + "|" + nonce + "|" + mac + "|" + payload;
            return true;
        }

        public static bool TryUnseal(string token, string message, TcpAuthNonceCache nonces, out string payload, out string error)
        {
            return TryUnseal(token, message, nonces, DateTimeOffset.UtcNow, out payload, out error);
        }

        public static bool TryUnseal(string token, string message, TcpAuthNonceCache nonces, DateTimeOffset utcNow, out string payload, out string error)
        {
            payload = "";
            error = "";
            if (IsRejectedToken(token))
            {
                error = "未配置有效 Token";
                return false;
            }

            if (string.IsNullOrEmpty(message) || !message.StartsWith(Version + "|", StringComparison.Ordinal))
            {
                error = "鉴权格式无效";
                return false;
            }

            string rest = message.Substring(Version.Length + 1);
            int p1 = rest.IndexOf('|');
            int p2 = p1 < 0 ? -1 : rest.IndexOf('|', p1 + 1);
            int p3 = p2 < 0 ? -1 : rest.IndexOf('|', p2 + 1);
            if (p1 <= 0 || p2 < 0 || p3 < 0)
            {
                error = "鉴权格式无效";
                return false;
            }

            string unixText = rest.Substring(0, p1);
            string nonce = rest.Substring(p1 + 1, p2 - p1 - 1);
            string mac = rest.Substring(p2 + 1, p3 - p2 - 1);
            payload = rest.Substring(p3 + 1);

            long unix;
            if (!long.TryParse(unixText, out unix))
            {
                error = "鉴权时间戳无效";
                payload = "";
                return false;
            }

            long now = utcNow.ToUnixTimeSeconds();
            if (unix > now + AllowedSkewSeconds || unix < now - AllowedSkewSeconds)
            {
                error = "鉴权时间戳超出允许范围";
                payload = "";
                return false;
            }

            if (!IsValidNonce(nonce))
            {
                error = "鉴权 nonce 无效";
                payload = "";
                return false;
            }

            string expected = ComputeMac(token.Trim(), unix, nonce, payload);
            if (!FixedTimeEquals(mac, expected))
            {
                error = "鉴权校验失败";
                payload = "";
                return false;
            }

            if (nonces == null || !nonces.TryRemember(nonce, now, AllowedSkewSeconds))
            {
                error = "鉴权报文已使用过";
                payload = "";
                return false;
            }

            return true;
        }

        public static string CreateNonce()
        {
            byte[] bytes = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        private static bool IsValidNonce(string nonce)
        {
            if (string.IsNullOrEmpty(nonce) || nonce.Length < 16 || nonce.Length > 128)
                return false;
            for (int i = 0; i < nonce.Length; i++)
            {
                char c = nonce[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                if (!ok)
                    return false;
            }

            return true;
        }

        private static string ComputeMac(string token, long unix, string nonce, string payload)
        {
            string material = unix.ToString() + "\n" + nonce + "\n" + payload;
            byte[] key = Encoding.UTF8.GetBytes(token);
            byte[] data = Encoding.UTF8.GetBytes(material);
            using (var hmac = new HMACSHA256(key))
            {
                byte[] hash = hmac.ComputeHash(data);
                return Convert.ToBase64String(hash);
            }
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            byte[] a = Encoding.UTF8.GetBytes(left ?? "");
            byte[] b = Encoding.UTF8.GetBytes(right ?? "");
            if (a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    public sealed class TcpAuthNonceCache
    {
        private readonly ConcurrentDictionary<string, long> _seen = new ConcurrentDictionary<string, long>();
        private long _nextPurgeUnix;

        public bool TryRemember(string nonce, long nowUnix, long ttlSeconds)
        {
            long expiry = nowUnix + ttlSeconds;
            while (true)
            {
                if (_seen.TryAdd(nonce, expiry))
                {
                    PurgeIfNeeded(nowUnix);
                    return true;
                }

                long existing;
                if (!_seen.TryGetValue(nonce, out existing))
                    continue;
                if (existing >= nowUnix)
                    return false;
                if (_seen.TryUpdate(nonce, expiry, existing))
                    return true;
            }
        }

        private void PurgeIfNeeded(long nowUnix)
        {
            if (nowUnix < _nextPurgeUnix)
                return;
            _nextPurgeUnix = nowUnix + 30;
            foreach (var pair in _seen)
            {
                if (pair.Value < nowUnix)
                {
                    long ignored;
                    _seen.TryRemove(pair.Key, out ignored);
                }
            }
        }
    }
}
