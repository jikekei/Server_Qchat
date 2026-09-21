@echo off
chcp 65001 >nul
title Server_Qcha.Daemon [LocalAdmin 独立守护进程]
cd /d "%~dp0"
echo ===================================================================
echo   正在启动 Qcha LocalAdmin 独立守护进程...
echo   本进程负责托管 SCPSL 游戏服并维持控制台长连接。
echo   即使关闭或重启 Server_Qcha.Bot 面板，游戏服也不会掉线！
echo ===================================================================
Server_Qcha.Daemon.exe
pause
