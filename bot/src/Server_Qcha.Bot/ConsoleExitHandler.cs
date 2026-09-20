using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Server.Qcat;

/// <summary>
/// 控制台退出拦截与多次确认保护：
/// 拦截 Ctrl+C、Ctrl+Break 以及控制台窗口右上角关闭按钮（X），
/// 弹出高危警告并要求多次确认，明确警示关闭 Server_Qcha.Bot.exe 会导致托管的所有游戏服务器全部掉线。
/// </summary>
public static class ConsoleExitHandler
{
    private delegate bool ConsoleCtrlDelegate(int sig);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const int CTRL_C_EVENT = 0;
    private const int CTRL_BREAK_EVENT = 1;
    private const int CTRL_CLOSE_EVENT = 2;
    private const int CTRL_LOGOFF_EVENT = 5;
    private const int CTRL_SHUTDOWN_EVENT = 6;

    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONWARNING = 0x00000030;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_DEFBUTTON2 = 0x00000100;
    private const uint MB_SYSTEMMODAL = 0x00001000;
    private const int IDYES = 6;

    private static ConsoleCtrlDelegate? _ctrlHandler;
    private static IHostApplicationLifetime? _lifetime;
    private static ILogger? _logger;
    private static bool _isExitingConfirmed = false;
    private static readonly object _exitLock = new();

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll")]
    private static extern bool DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

    private const uint SC_CLOSE = 0xF060;
    private const uint MF_BYCOMMAND = 0x00000000;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_DISABLED = 0x00000002;

    public static void Initialize(IHostApplicationLifetime lifetime, ILogger logger)
    {
        _lifetime = lifetime;
        _logger = logger;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // 核心修复：禁用控制台窗口右上角关闭按钮（X）与 Alt+F4
            // 彻底防止 Windows 系统在点 X 后强制在 5 秒内杀死进程导致点“否”依然退出的问题
            DisableConsoleCloseButton();

            _ctrlHandler = HandleConsoleCtrl;
            SetConsoleCtrlHandler(_ctrlHandler, true);
        }

        Console.CancelKeyPress += OnCancelKeyPress;
    }

    /// <summary>
    /// 禁用控制台窗口的关闭按钮（X），防止误触或因 Windows 5 秒超时强制杀死进程。
    /// </summary>
    public static void DisableConsoleCloseButton()
    {
        try
        {
            IntPtr hWnd = GetConsoleWindow();
            if (hWnd != IntPtr.Zero)
            {
                IntPtr hMenu = GetSystemMenu(hWnd, false);
                if (hMenu != IntPtr.Zero)
                {
                    DeleteMenu(hMenu, SC_CLOSE, MF_BYCOMMAND);
                    EnableMenuItem(hMenu, SC_CLOSE, MF_BYCOMMAND | MF_GRAYED | MF_DISABLED);
                }
            }
        }
        catch
        {
            // 忽略非交互控制台环境异常
        }
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // 阻止默认的直接终止进程
        e.Cancel = true;

        lock (_exitLock)
        {
            if (_isExitingConfirmed)
                return;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("================================================================================");
            Console.WriteLine("【高危警告】检测到正在尝试关闭 Server_Qcha.Bot.exe 主程序！");
            Console.WriteLine("【严重后果说明】：");
            Console.ResetColor();

            // 1 号警告红色高亮（红底白字超显眼）
            Console.BackgroundColor = ConsoleColor.DarkRed;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  1. 托管的所有 SCPSL 游戏服务器进程将被全部强制终止，全部在线玩家将立即掉线！  ");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  2. QQ 机器人服务与 Web 控制面板将全部停止运行！");
            Console.WriteLine("================================================================================");
            Console.ResetColor();

            // 第 1 次确认提示（红色高亮）
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("【第 1 次确认 / 共 2 次】确定要关闭程序并导致所有服务器全部掉线吗？(输入 Y 确认，输入其它任意键取消): ");
            Console.ResetColor();

            string? input1 = Console.ReadLine()?.Trim();
            if (!string.Equals(input1, "Y", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(">> 已取消关闭操作，Server_Qcha.Bot.exe 继续正常运行。\n");
                Console.ResetColor();
                return;
            }

            // 第 2 次确认提示（红色高亮）
            Console.BackgroundColor = ConsoleColor.DarkRed;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("【第 2 次确认 / 共 2 次】请再次输入 YES 进行最终确认 (输入 YES 立即关闭并断开所有服务器): ");
            Console.ResetColor();
            Console.Write(" ");

            string? input2 = Console.ReadLine()?.Trim();
            if (!string.Equals(input2, "YES", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(">> 二次确认未匹配，已取消关闭操作，Server_Qcha.Bot.exe 继续正常运行。\n");
                Console.ResetColor();
                return;
            }

            _isExitingConfirmed = true;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(">> 用户已完成两次确认，正在关闭程序并安全断开所有服务器...");
            Console.ResetColor();

            Task.Run(() =>
            {
                _lifetime?.StopApplication();
            });
        }
    }

    private static bool HandleConsoleCtrl(int sig)
    {
        if (sig == CTRL_C_EVENT || sig == CTRL_BREAK_EVENT)
        {
            // 由 Console.CancelKeyPress 统一做交互式多次输入确认
            return false;
        }

        if (sig == CTRL_CLOSE_EVENT)
        {
            if (_isExitingConfirmed)
                return false;

            // 第一次确认弹窗
            int res1 = MessageBox(
                IntPtr.Zero,
                "检测到您正在尝试关闭 Server_Qcha.Bot.exe 控制台窗口！\n\n" +
                "【严重后果说明】：\n" +
                "1. 关闭本程序会导致托管的所有 SCPSL 游戏服务器全部被关闭！\n" +
                "2. 正在游戏中的所有在线玩家将立即全部掉线断开连接！\n" +
                "3. Web 管理面板与 QQ 机器人将立即停止！\n\n" +
                "确定要继续关闭程序吗？",
                "高危警告：关闭 Server_Qcha.Bot 确认 (第 1 次确认 / 共 2 次)",
                MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2 | MB_SYSTEMMODAL);

            if (res1 != IDYES)
            {
                // 用户取消，阻止关闭
                return true;
            }

            // 第二次确认弹窗
            int res2 = MessageBox(
                IntPtr.Zero,
                "【最终二次确认】：\n\n" +
                "请再次确认：点击“是”将立即关闭 Server_Qcha.Bot.exe，\n" +
                "所有游戏服务器将全部停止，所有在线玩家将全部掉线！\n\n" +
                "确定执行最终关闭吗？",
                "最终确认：关闭 Server_Qcha.Bot (第 2 次确认 / 共 2 次)",
                MB_YESNO | MB_ICONERROR | MB_DEFBUTTON2 | MB_SYSTEMMODAL);

            if (res2 != IDYES)
            {
                // 用户取消，阻止关闭
                return true;
            }

            _isExitingConfirmed = true;
            _lifetime?.StopApplication();
            return false; // 允许正常关闭
        }

        return false;
    }
}
