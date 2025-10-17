# SocketServer-SCPSL
<a href="https://github.com/YF-OFFICE/SocketServer-SCPSL/releases"><img src="https://img.shields.io/github/v/release/YF-OFFICE/SocketServer-SCPSL?display_name=tag&style=for-the-badge&logo=gitbook&label=Release" href="https://github.com/YF-OFFICE/SocketServer-SCPSL/releases" alt="GitHub Releases"></a>
<img src="https://img.shields.io/github/downloads/YF-OFFICE/SocketServer-SCPSL/total?style=for-the-badge&logo=github" alt="Downloads">

一个可以将QQ群与服务端连接的插件

# 如有bug请在[Iss](https://github.com/YF-OFFICE/SocketServer-SCPSL/issues)里提出


~~Warn:本权限组只允许群主使用Round指令  插件和程序正常运行 只是需要一个qq客户端的正向Websocket8080端口来链接 替换Cq客户端的方法在下方 当然你也可以自行寻找 只要是能有正向WebSocket8080端口就ok~~


目前所有拥有的指令:cx,info，round(暂不支持)

~~增加round指令
round list查询玩家列表
round rest 重启回合
round start 启动回合
round allrest 重启服务器
round kick+id 踢出对应id玩家
round bc+text 向服务器发送广播
Warn:目前round指令只允许Q群群主使用 如有其他需求请在issues里提出~~


说明：
本插件运用Socket将[SCPSL](scpslgame.com)

基于[EXILED](https://github.com/Exiled-Team/EXILED/)开发

基于[EleCho.GoCqHttpSdk](https://github.com/OrgEleCho/EleCho.GoCqHttpSdk)开发

查询功能与QQ群里连接起来;（也就是CX功能）



使用方法:


在[Releases](https://github.com/NLK-TeamOffice/SocketServer-SCPSL/releases/)里找到最新版本并下载


1.将SocketServer.dll文件放到exiled/plugin里


2.打开服务器(确保对应的端口是打开的) 【如有多个服务器请在 对应exconfig里修改端口和name】

```cs
        public int TcpPort { get; set; } = 10087;
        public string IP { get; set; } = "127.0.0.1";
        public string name { get; set; } = "1服";
        public bool IsEnabled { get ; set ; }=true;
        public bool Debug { get ; set ; } = false;
```

~~~~
因为现Gocqhttp不能用 提供两种解决方法
~~~~

=====================================================


①.使用OpenSharmy框架(缺点占用服务器后台内存可能会很大) ~~提供教程链接 :[BiliBIli](https://www.bilibili.com/video/BV17m41197tQ)~~

教程视频已经不在 请到[点我](https://github.com/YF-OFFICE/SocketServer-SCPSL/blob/main/Yee.md)查看教程

Warn:不用安装OverFlow 到转换端口那一步 将端口5800专向8080端口 然后模拟器保持在后台运行


②.使用qq官方框架(例如:gensokyo框架)(缺点:需要自己注册一个qq机器人)


③.最推荐 使用[NapNeko](https://github.com/NapNeko/NapCatQQ)机器人登录 并且在初次运行后 将config里的ws改为6700



```cs
如:"ws": {
    "enable": true,
    "host": "127.0.0.1",
    "port": 6700
```
=========================================================

4.解压GoHttpqq-Socket.zip


5.打开GoHttpqq-Socket.exe

6.先输入你想要查询的服务器 如有多个请用*隔开 如只有一个就只写一个 

7.写入能查询的群号 规则同上↑

回车确定 挂在后台

就可以了 可以说是暂时代替 除了round暂时不能用 其他功能均正常运行

6.在群中发送cx，info即可获取服务器信息


