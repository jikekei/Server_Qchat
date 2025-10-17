[如果本文档看不清请点我转到原文链接](https://wiki.mrxiaom.top/overflow/openshamrock/mumu.html) 


只需完成文档教程中1~5步 转发端口记得要将手机端的5800端口改到8080端口或是都配置8080端口 正向web Overflow不必安装

1.安装模拟器
正常安装 Mumu 模拟器，本文使用版本是 Mumu 12 V3.7.3 (2511)https://mumu.163.com/update/

#2.安装QQ
选择一个版本相对较低，且可以登录的 QQ，并安装。本文使用的是官方渠道的 8.9.80.12440。https://downv6.qq.com/qqweb/QQ_1/android_apk/Android_8.9.80_64.apk

安装完成后打开登录需要做机器人的账号即可。

QQ 版本越高检测越严，不要更新。

#3.连接ADB
另请参见 官方教程。

简单来说，就是先在右上角菜单点击问题诊断，在差不多最下面的网络信息中找到ADB调试端口，一般是16384，有冲突或者多开时端口不一样。

然后打开 Mumu 安装路径，默认的是 D:\Program Files\Netease\MuMuPlayer-12.0\shell，打开后编辑地址栏，输入 cmd 并回车。

在命令窗口中输入命令 
~~~~
adb connect 127.0.0.1:调试端口
~~~~
即可连接到 ADB。

执行完不要关闭命令窗口，先留着，后面要用。

#4.安装OpenShamrock
~~~~

1.获取权限
打开 Mumu 设置

在 磁盘设置 开启 可写系统盘
在 其它设置 开启 手机Root权限 保存设置并重启模拟器。
#2.安装Magisk https://github.com/HuskyDG/magisk-files/releases
下载不要带lite版本的削减版
下载其 APK，在 Mumu 安装并打开，请求 Root 权限时选择始终允许。

第一次授权 Root 状态不会刷新，退出并重新打开 Magisk，依次点击

安装
直接安装 (直接修改/system)
开始
重启
模拟器自带 Root 和 Magisk 的 Root 可能会有冲突，若出现相关提示，忽略即可。

点击右上角设置，开启 Zygisk，其它设置根据个人喜好修改。
3.安装LSPosed https://github.com/LSPosed/LSPosed/releases
到 LSPosed 发布页 下载 zygisk-release.zip 结尾的文件，复制到 此电脑/文档/Mumu共享文件夹 中。

打开 Magisk，在 模块 点击 从本地安装，在左上角菜单点击 下载 下面那个设备名选项 (在笔者的模拟器中是 NCO-AL00，不保证在别的版本中一致。)。

点击 $Mumu12Shared，选择刚刚复制进去的文件，等待安装。

同样的，安装结束后不要点击重启，手动关闭模拟器再打开模拟器。

#4.安装OpenShamrock
选择一个渠道下载 OpenShamrock (下载 -all 的那个文件)

发布版
开发版 https://github.com/whitechi73/OpenShamrock/releases
安装完毕后，至少启动一遍 Shamrock，在 状态 中开启 主动WebSocket。

然后使用上游文档第3步的方法连接ADB，并在命令窗口执行以下命令打开 LSPosed。


~~~~
adb shell am start -a android.intent.action.MAIN -c org.lsposed.manager.LAUNCH_MANAGER -n com.android.shell/.BugreportWarningActivity
~~~~
(需要这么做的原因: LSPosed discussions#2729)

在 模块 处启用 Shamrock，然后关闭再打开模拟器。启动QQ，返回 Shamrock，主页显示 已激活 则代表安装成功。

警告

修改 状态 中的 接口信息 配置需要完全重启QQ才生效，建议结束QQ进程或重启模拟器来使配置生效。

#完成
安装完成，请返回上游文档从第5步开始操作。

~~~~
