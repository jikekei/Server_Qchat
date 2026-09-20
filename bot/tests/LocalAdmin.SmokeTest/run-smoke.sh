#!/usr/bin/env bash
# LocalAdmin 面板冒烟测试一键脚本（Git Bash / MSYS2）
#
#   构建假游戏 → 复制到 ASCII 暂存目录 → 以隔离的测试配置启动机器人 → 跑断言 → 清理进程
#
# 用法：
#   bash bot/tests/LocalAdmin.SmokeTest/run-smoke.sh
#
# 可选环境变量：
#   SMOKE_PORT   面板端口（默认 8099）
#   SMOKE_WORK   暂存目录（默认 %TEMP%/localadmin-smoke）
#   DOTNET       dotnet 可执行文件路径
#   PYTHON       python 可执行文件路径
#
# 注意事项：
#   1. 暂存目录必须放在**不含非 ASCII 字符**的路径下。MSBuild 无法解析含中文的
#      绝对路径参数（会报「响应文件追加的开关 …」），因此构建一律用相对路径，
#      运行期产物则复制到 %TEMP% 下。
#   2. 本脚本使用独立的账号库与日志目录，不读写生产配置，也不会触碰真实游戏服务端的任何数据。
set -u
export PATH="/usr/bin:/bin:$PATH"

HERE="${BASH_SOURCE[0]%/*}"
[ -z "$HERE" ] && HERE="."
HERE="$(cd "$HERE" && pwd)"
# 传给原生 Windows 进程（python.exe）时必须用 Windows 风格路径，
# 否则 /c/... 会被解释成当前盘符下的 \c\... 而找不到文件
HERE_WIN="$(cd "$HERE" && pwd -W 2>/dev/null || echo "$HERE")"
ROOT="$(cd "$HERE/../.." && pwd)"                 # bot/
BOT_DIR="$ROOT/src/Server_Qcha.Bot"
PORT="${SMOKE_PORT:-8099}"
STAGE="${SMOKE_WORK:-/c/Users/${USERNAME:-$USER}/AppData/Local/Temp/localadmin-smoke}"

DOTNET="${DOTNET:-/c/Program Files/dotnet/dotnet.exe}"
command -v "$DOTNET" >/dev/null 2>&1 || [ -x "$DOTNET" ] || DOTNET="dotnet"

PYTHON="${PYTHON:-}"
if [ -z "$PYTHON" ]; then
    for c in "/c/Users/${USERNAME:-$USER}/.workbuddy/binaries/python/versions/3.13.12/python.exe" python py; do
        if command -v "$c" >/dev/null 2>&1 || [ -x "$c" ]; then PYTHON="$c"; break; fi
    done
fi
if [ -z "$PYTHON" ]; then
    echo "找不到 python，请通过 PYTHON=... 指定路径" >&2
    exit 2
fi

mkdir -p "$STAGE"
STAGE_WIN="$(cd "$STAGE" && pwd -W)"
WORK="$STAGE/work"
FAKE_EXE="$STAGE_WIN/fakegame/FakeGame.exe"
LOG="$WORK/bot.log"
BOT_PID=""

cleanup() {
    # 只结束本脚本启动的进程。
    # 注意：这里刻意**不**按进程名 taskkill —— 那会把你正在跑的生产机器人一起杀掉。
    [ -n "$BOT_PID" ] && kill "$BOT_PID" 2>/dev/null || true
    taskkill //F //IM FakeGame.exe >/dev/null 2>&1 || true
}
trap cleanup EXIT

step() { printf '\n=== %s ===\n' "$1"; }

cd "$ROOT" || exit 1

step "构建 FakeGame"
"$DOTNET" build "tests/LocalAdmin.SmokeTest/FakeGame/FakeGame.csproj" -c Release --nologo 2>&1 | tail -4 || exit 1
if [ ! -f "$HERE/FakeGame/bin/Release/net8.0/FakeGame.exe" ]; then
    echo "FakeGame.exe 未生成" >&2
    exit 1
fi

step "构建机器人"
"$DOTNET" build "src/Server_Qcha.Bot/Server_Qcha.Bot.csproj" -c Release --nologo 2>&1 | tail -4 || exit 1

step "准备隔离运行目录"
rm -rf "$WORK" "$STAGE/fakegame"
mkdir -p "$WORK/logs" "$STAGE/fakegame"
cp -f "$HERE/FakeGame/bin/Release/net8.0/FakeGame.exe" "$STAGE/fakegame/"
cp -f "$HERE/FakeGame/bin/Release/net8.0/FakeGame.dll" "$STAGE/fakegame/" 2>/dev/null || true
cp -f "$HERE/FakeGame/bin/Release/net8.0/FakeGame.runtimeconfig.json" "$STAGE/fakegame/" 2>/dev/null || true

export LocalAdmin__Enabled=true
export LocalAdmin__Servers__0__Id=demo
export LocalAdmin__Servers__0__Name=SmokeTest
export LocalAdmin__Servers__0__ExecutablePath="$FAKE_EXE"
export LocalAdmin__Servers__0__GamePort=7799
export LocalAdmin__Servers__0__EnableHeartbeat=true
export LocalAdmin__Servers__0__HeartbeatSpanMaxThreshold=6
export LocalAdmin__Servers__0__HeartbeatRestartInSeconds=3
export LocalAdmin__Servers__0__RestartLimit=3
export LocalAdmin__Servers__0__RestartTimeWindowSeconds=120
export LocalAdmin__Servers__0__GracefulStopTimeoutSeconds=8
export LocalAdmin__Servers__0__RedirectStandardStreams=true
export LocalAdmin__LogDirectory="$STAGE_WIN/work/logs"
export LocalAdmin__ConsoleBufferLines=500
export WebPanel__Enabled=true
export WebPanel__Host=127.0.0.1
export WebPanel__Port="$PORT"
export WebPanel__DatabasePath="$STAGE_WIN/work/panel-smoke.db"
# 通知通道改到测试端口，避免与可能正在运行的生产机器人抢占 10088
export SocketServer__NotificationPort="${SMOKE_NOTIFY_PORT:-10098}"

step "启动机器人（面板 127.0.0.1:$PORT）"
( cd "$BOT_DIR/bin/Release/net8.0" && exec "$DOTNET" Server_Qcha.Bot.dll ) > "$LOG" 2>&1 &
BOT_PID=$!

for _ in $(seq 1 60); do
    grep -q "Application started" "$LOG" 2>/dev/null && break
    sleep 1
done

if ! grep -q "Application started" "$LOG" 2>/dev/null; then
    echo "机器人未能启动，日志尾部：" >&2
    tail -25 "$LOG" >&2
    echo >&2
    echo "提示：若提示端口被占用，请先结束残留的机器人进程（netstat -ano | findstr :$PORT）。" >&2
    exit 1
fi

step "运行断言"
SMOKE_BASE="http://127.0.0.1:$PORT" \
SMOKE_LOG="$STAGE_WIN/work/bot.log" \
SMOKE_ID=demo \
"$PYTHON" "$HERE_WIN/smoke-test.py"
RC=$?

step "完成"
if [ "$RC" -eq 0 ]; then
    echo "全部通过"
else
    echo "存在失败项（退出码 $RC），机器人日志：$LOG"
fi
exit "$RC"
