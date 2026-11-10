#if NET8_0_OR_GREATER
#nullable disable
#endif
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Qchat.GameAdmin
{
    public static class AdminFrame
    {
        public const int MaxLength = 2 * 1024 * 1024;
        // Requests are received before HMAC verification. Responses may contain much larger snapshots.
        public const int MaxRequestLength = 300 * 1024;
        public static readonly byte[] Magic = Encoding.ASCII.GetBytes("QGA1");
        public static async Task ReadExactly(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            while (count > 0)
            {
                var read = await stream.ReadAsync(buffer, offset, count, ct).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("权限报文不完整");
                offset += read; count -= read;
            }
        }
        public static async Task<string> ReadBody(Stream stream, CancellationToken ct, int maxLength = MaxLength)
        {
            ValidateLimit(maxLength);
            var length = new byte[4];
            await ReadExactly(stream, length, 0, 4, ct).ConfigureAwait(false);
            int count = (length[0] << 24) | (length[1] << 16) | (length[2] << 8) | length[3];
            if (count < 1 || count > maxLength) throw new InvalidDataException("权限报文大小无效");
            var body = new byte[count];
            await ReadExactly(stream, body, 0, count, ct).ConfigureAwait(false);
            return new UTF8Encoding(false, true).GetString(body);
        }
        public static async Task<string> Read(Stream stream, CancellationToken ct)
        {
            var magic = new byte[4];
            await ReadExactly(stream, magic, 0, 4, ct).ConfigureAwait(false);
            if (Encoding.ASCII.GetString(magic) != "QGA1") throw new InvalidDataException("插件不支持权限协议，请升级插件");
            return await ReadBody(stream, ct).ConfigureAwait(false);
        }
        public static async Task Write(Stream stream, string text, CancellationToken ct, int maxLength = MaxLength)
        {
            ValidateLimit(maxLength);
            int n = Encoding.UTF8.GetByteCount(text);
            if (n < 1 || n > maxLength) throw new InvalidDataException("权限配置过大");
            var body = Encoding.UTF8.GetBytes(text);
            var header = new byte[] { 81, 71, 65, 49, (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n };
            await stream.WriteAsync(header, 0, header.Length, ct).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        }
        private static void ValidateLimit(int maxLength)
        {
            if (maxLength < 1 || maxLength > MaxLength) throw new ArgumentOutOfRangeException(nameof(maxLength));
        }
    }
}
