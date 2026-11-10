// Test-only facades for linked plugin source; no game/framework assembly is loaded by the tests.
namespace Exiled.API.Features
{
    internal static class Log
    {
        public static void Error(string message) { }
        public static void Warn(string message) { }
        public static void Debug(string message) { }
    }
}
namespace SocketServer
{
    internal static class Log
    {
        public static void Error(string message) { }
        public static void Warn(string message) { }
        public static void Debug(string message) { }
    }
}
