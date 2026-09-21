using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

internal static class DaemonExitHandler
{
    private delegate bool ConsoleCtrlDelegate(int signal);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool revert);

    [DllImport("user32.dll")]
    private static extern bool EnableMenuItem(IntPtr hMenu, uint item, uint flags);

    private const int CtrlC = 0;
    private const int CtrlBreak = 1;
    private const int CtrlClose = 2;
    private const uint MbYesNo = 0x00000004;
    private const uint MbIconWarning = 0x00000030;
    private const uint MbDefaultButton2 = 0x00000100;
    private const uint MbSystemModal = 0x00001000;
    private const int IdYes = 6;
    private const uint MfByCommand = 0x00000000;
    private const uint MfGrayed = 0x00000001;
    private const uint MfDisabled = 0x00000002;

    private static readonly object Gate = new();
    private static IHostApplicationLifetime? _lifetime;
    private static ConsoleCtrlDelegate? _handler;
    private static bool _confirmed;

    public static void Initialize(IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        _handler = HandleConsoleControl;

        if (OperatingSystem.IsWindows())
        {
            SetConsoleCtrlHandler(_handler, true);
            DisableConsoleCloseButton();
        }
    }

    private static bool HandleConsoleControl(int signal)
    {
        if (signal is CtrlC or CtrlBreak)
        {
            // 原生回调必须明确返回 true。若返回 false，Windows 会继续执行默认
            // 退出流程，导致用户在确认提示中点“否”后仍被强制关闭。
            ConfirmAndStop(useMessageBox: false);
            return true;
        }

        if (signal == CtrlClose)
        {
            ConfirmAndStop(useMessageBox: true);
            return true;
        }

        return false;
    }

    private static void DisableConsoleCloseButton()
    {
        // Windows 控制台关闭按钮的 CTRL_CLOSE_EVENT 无法可靠地被取消：
        // 用户点击“否”后系统仍可能在超时后强制结束进程。因此禁用 X，
        // 关闭 Daemon 必须使用 Ctrl+C 或面板/脚本中的确认流程。
        IntPtr window = GetConsoleWindow();
        if (window == IntPtr.Zero)
            return;

        IntPtr menu = GetSystemMenu(window, false);
        if (menu != IntPtr.Zero)
            EnableMenuItem(menu, 0xF060, MfByCommand | MfGrayed | MfDisabled);
    }

    private static void ConfirmAndStop(bool useMessageBox)
    {
        lock (Gate)
        {
            if (_confirmed)
                return;

            bool confirmed;
            if (useMessageBox)
            {
                int result = MessageBox(
                    IntPtr.Zero,
                    "即将关闭 Server_Qcha.Daemon。\n\n" +
                    "【高危警告】关闭守护进程会导致它托管的全部 SCPSL 游戏服务器停止，在线玩家将全部掉线。\n\n" +
                    "如果只是维护或关闭 Server_Qcha 主程序，请取消本操作。\n\n" +
                    "确定要关闭守护进程吗？",
                    "高危警告：关闭 Server_Qcha.Daemon",
                    MbYesNo | MbIconWarning | MbDefaultButton2 | MbSystemModal);
                confirmed = result == IdYes;
            }
            else
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("================================================================================");
                Console.WriteLine("【高危警告】正在尝试关闭 Server_Qcha.Daemon！");
                Console.WriteLine("关闭守护进程会导致其托管的全部 SCPSL 游戏服务器停止，在线玩家将全部掉线。");
                Console.WriteLine("如果只是维护或关闭 Server_Qcha 主程序，请取消本操作。");
                Console.WriteLine("================================================================================");
                Console.ResetColor();
                Console.Write("确定要关闭守护进程吗？(输入 Y 确认，其它内容取消): ");
                confirmed = string.Equals(Console.ReadLine()?.Trim(), "Y", StringComparison.OrdinalIgnoreCase);
            }

            if (!confirmed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(">> 已取消关闭，Server_Qcha.Daemon 继续运行。");
                Console.ResetColor();
                return;
            }

            _confirmed = true;
            Console.WriteLine(">> 正在安全关闭 Server_Qcha.Daemon...");
            _lifetime?.StopApplication();
        }
    }
}
