# Server_Qchat
Serer_Qchat 一个简单的可以将SCP:SL服务器连接到q群的机器人，其中还包括很多实用功能
功能介绍：

cx可以查询服务器人数以及在线管理，

info可以查询服务器信息

#1，#xx等可以查询服务器玩家列表

玩家输入信息包含"炸了？""服务器炸了？"会发送服务器当前状态以及延迟，

玩家输入信息包含“赞助”会发送赞助的信息详细

-----------------------------------------------------------------------------
预览功能

使用SocketServerAsync监听事件，实现了在服务器控制台输入.ac自动同步发送到q群

添加数据库连接功能，配合插件可以实现查询游玩时间，杀人数量等功能（但是有点问题

添加API的支持，可以通过调用api了实现查询人数，而不用麻烦的部署插件


本程序包括3部分

1.登录QQ

推荐使用https://github.com/NapNeko/NapCatQQ QQ机器人框架，当然其他框架也可以

按照 https://github.com/NapNeko/NapCatQQ 提供的方法登录，

如何修改配置文件如同，在config目录下“onebot11_QQ号”配置文件，如下所示修改

![image](https://github.com/user-attachments/assets/507a784a-fa30-4824-9ba4-a8ec9d658aa7)

2.安装插件
下载插件 CX查询插件，安装到exiled的插件文件夹，并修改配置文件

3.使用中继程序 Server_Qchat_exe
Server_Qchat_exe需要运行库net6.0
下载地址 https://dotnet.microsoft.com/zh-cn/download/dotnet/6.0
如果出现打开闪退请下载运行库合集
https://www.52pojie.cn/thread-1916914-1-1.html
打开，并输入服务器端口号，以及群号就可以用了。

如果出现任何问题或者不会的可以与我联系：QQ ：3037240065 邮箱：liseximt@outlook.com
