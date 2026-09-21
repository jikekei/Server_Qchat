@echo off
chcp 65001 >nul
title 停止 Qcha 守护进程
cd /d "%~dp0"
echo ===================================================================
echo   【高危警告】停止 LocalAdmin 独立守护进程 (Server_Qcha.Daemon.exe)
echo ===================================================================
echo.
echo   警告：停止守护进程将导致受其托管的全部 SCPSL 游戏服务器一并退出，
echo   正在游玩的全体在线玩家将全部掉线断开连接！
echo.
echo   提示：若仅需更新、重启或维护 Web 控制台与机器人，游戏服仍可常驻运行，
echo   切勿轻易停止守护进程。
echo.
echo ===================================================================
set /p confirm=确定要停止守护进程吗？(输入 Y 确认关闭，输入其他任意键取消): 
if /i not "%confirm%"=="Y" (
    echo.
    echo   [已取消] 操作已中止，守护进程继续保持运行。
    echo.
    pause
    exit /b
)
echo.
echo   正在停止守护进程...
taskkill /f /im Server_Qcha.Daemon.exe 2>nul
if %errorlevel% equ 0 (
    echo.
    echo   [成功] 守护进程已停止！
) else (
    echo.
    echo   [提示] 守护进程当前并未在运行。
)
echo ===================================================================
echo 提示：若需彻底结束所有游戏服，可配合面板停止或在任务管理器中结束。
echo.
pause
