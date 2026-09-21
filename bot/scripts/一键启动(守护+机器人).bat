@echo off
chcp 65001 >nul
title Qcha 启动管理器
cd /d "%~dp0"
echo ===================================================================
echo   [1/2] 正在启动 Qcha LocalAdmin 独立守护进程 (Server_Qcha.Daemon)...
echo ===================================================================
start "Server_Qcha.Daemon [守护进程-保持游戏服在线]" Server_Qcha.Daemon.exe
timeout /t 2 /nobreak >nul
echo.
echo ===================================================================
echo   [2/2] 正在启动 Qcha QQ机器人与Web控制面板 (Server_Qcha.Bot)...
echo ===================================================================
start "Server_Qcha.Bot [面板与QQ机器人]" Server_Qcha.Bot.exe
echo.
echo 启动完成！
echo 提示：
echo - 游戏服在守护进程窗口中受保护运行，更新或重启机器人不会影响游戏服。
echo - 访问控制台面板：http://127.0.0.1:8080
timeout /t 3 >nul
