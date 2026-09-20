// 假游戏服务端：复刻 SCPSL 的 ServerOutput.TcpConsole 上行帧格式与下行命令读取，
// 用于端到端验证 LocalAdmin 面板实现（帧编解码 / 心跳 / 退出动作协商 / 重启策略）。
//
// 支持的命令：
//   exit      → 发 0x15 SilentShutdown 后正常退出
//   shutdown  → 发 0x14 Shutdown 后正常退出
//   reboot    → 发 0x16 Restart 后正常退出
//   crash     → 发 0x13 ExitActionReset 后以退出码 17 异常退出
//   其它      → 以颜色 11 回显 "[ECHO] <命令>"
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

// 让被测端能稳定地以 UTF-8 读取本进程的标准输出（默认是系统 ANSI 代码页）
Console.OutputEncoding = Encoding.UTF8;

int consolePort = 0;
int gamePort = 0;
bool heartbeat = false;
bool disableAnsi = false;

foreach (string arg in args)
{
    if (arg.StartsWith("-console", StringComparison.OrdinalIgnoreCase)) int.TryParse(arg[8..], out consolePort);
    else if (arg.StartsWith("-port", StringComparison.OrdinalIgnoreCase)) int.TryParse(arg[5..], out gamePort);
    else if (arg.Equals("-heartbeat", StringComparison.OrdinalIgnoreCase)) heartbeat = true;
    else if (arg.Equals("-disableAnsiColors", StringComparison.OrdinalIgnoreCase)) disableAnsi = true;
}

Console.WriteLine($"[FakeGame] pid={Environment.ProcessId} consolePort={consolePort} gamePort={gamePort} heartbeat={heartbeat} disableAnsi={disableAnsi}");
Console.WriteLine($"[FakeGame] raw args: {string.Join(' ', args)}");
Console.Out.Flush();

if (consolePort <= 0)
{
    Console.Error.WriteLine("[FakeGame] 缺少 -console 参数，退出");
    return 2;
}

using var client = new TcpClient();
for (int i = 0; i < 50 && !client.Connected; i++)
{
    try { await client.ConnectAsync("127.0.0.1", consolePort); }
    catch (SocketException) { await Task.Delay(200); }
}

if (!client.Connected)
{
    Console.Error.WriteLine("[FakeGame] 无法连接控制台端口，退出");
    return 3;
}

client.NoDelay = true;
NetworkStream stream = client.GetStream();
Console.WriteLine("[FakeGame] 控制台通道已连接");

// ---- 上行帧：1 字节颜色码 + 4 字节小端长度 + UTF-8 正文 ----
async Task SendTextAsync(byte color, string text)
{
    byte[] body = Encoding.UTF8.GetBytes(text);
    byte[] buf = new byte[5 + body.Length];
    buf[0] = color;
    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(1, 4), body.Length);
    body.CopyTo(buf, 5);
    await stream.WriteAsync(buf);
    await stream.FlushAsync();
}

// ---- 上行帧：控制码，仅 1 字节 ----
async Task SendControlAsync(byte code)
{
    await stream.WriteAsync(new[] { code });
    await stream.FlushAsync();
}

await SendTextAsync(15, "Welcome to SCP: Secret Laboratory Dedicated Server (FAKE)");
await SendTextAsync(11, $"游戏端口 {gamePort} | 玩家上限 30");
await SendTextAsync(14, "警告：这是一条测试警告");
await SendTextAsync(12, "错误：这是一条测试错误");
await SendTextAsync(7, "多行测试：第一行\n第二行\n第三行");
await SendControlAsync(0x10); // RoundRestart

using var cts = new CancellationTokenSource();
Task? hbTask = null;
if (heartbeat)
{
    hbTask = Task.Run(async () =>
    {
        while (!cts.IsCancellationRequested)
        {
            await Task.Delay(3000, cts.Token);
            try { await SendControlAsync(0x17); }
            catch { return; }
        }
    });
}

// ---- 下行帧：4 字节小端长度 + UTF-8 正文（无类型码）----
try
{
    var lenBuf = new byte[4];
    while (true)
    {
        await stream.ReadExactlyAsync(lenBuf, 0, 4);
        int len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        if (len is < 0 or > 1_000_000)
        {
            Console.WriteLine($"[FakeGame] 非法长度 {len}");
            break;
        }

        var body = new byte[len];
        await stream.ReadExactlyAsync(body, 0, len);
        string command = Encoding.UTF8.GetString(body);
        Console.WriteLine($"[FakeGame] 收到命令: {command}");
        Console.Out.Flush();

        if (command.Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            await SendControlAsync(0x15);
            break;
        }

        if (command.Equals("crash", StringComparison.OrdinalIgnoreCase))
        {
            await SendControlAsync(0x13);
            await Task.Delay(120);
            Console.WriteLine("[FakeGame] 模拟崩溃退出");
            Environment.Exit(17);
        }

        if (command.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
        {
            await SendControlAsync(0x14);
            break;
        }

        if (command.Equals("reboot", StringComparison.OrdinalIgnoreCase))
        {
            await SendControlAsync(0x16);
            break;
        }

        await SendTextAsync(11, $"[ECHO] {command}");
    }
}
catch (EndOfStreamException)
{
    Console.WriteLine("[FakeGame] 控制台通道被关闭");
}
catch (Exception ex)
{
    Console.WriteLine($"[FakeGame] 读取异常: {ex.GetType().Name}: {ex.Message}");
}
finally
{
    cts.Cancel();
    if (hbTask is not null)
    {
        try { await hbTask; } catch { /* ignore */ }
    }
}

Console.WriteLine("[FakeGame] 退出");
return 0;
