using LabApi.Features.Console;

namespace SocketServer
{
    /// <summary>
    /// 统一日志门面，转发到 LabAPI 的 <see cref="Logger"/>。
    /// <para>
    /// 保留 EXILED 时代 <c>Log.Info / Warn / Error / Debug</c> 的调用风格，
    /// 这样除了 using 之外，业务代码的日志行不需要逐条改写，迁移 diff 更易审查。
    /// </para>
    /// </summary>
    internal static class Log
    {
        public static void Info(string message) => Logger.Info(message);

        public static void Warn(string message) => Logger.Warn(message);

        public static void Error(string message) => Logger.Error(message);

        /// <summary>
        /// 调试日志。LabAPI 的 <see cref="Logger.Debug(object, bool)"/> 需要显式传开关，
        /// 这里统一绑定到 config.yml 的 <c>debug</c>。
        /// </summary>
        public static void Debug(string message) => Logger.Debug(message, Main.Instance?.Config?.Debug ?? false);
    }
}
