这是一个为了简化部署的版本，只保留了核心的人数查询功能
1.登录QQ

推荐使用https://github.com/NapNeko/NapCatQQ QQ机器人框架，当然其他框架也可以

按照 https://github.com/NapNeko/NapCatQQ 提供的方法登录，

如何修改配置文件如同，在config目录下“onebot11_QQ号”配置文件，如下所示修改

![image](https://github.com/user-attachments/assets/507a784a-fa30-4824-9ba4-a8ec9d658aa7)

也就是开启ws功能端口为6700

2.下载程序API版本设置配置文件
打开app.config文件，格式如下

          《?xml version="1.0" encoding="utf-8" ?>
          《configuration>
          《appSettings>
          《add key="ServerPorts" value="1*2*3" />
          《add key="ServerName1" value="1服" />
          《add key="ServerName2" value="2服" />
          《add key="ServerName3" value="3服" />
          《/appSettings>
          《/configuration>

其中ServerPorts填写对应的serverid以*号为隔断如1*2*3

          《add key="ServerName1" value="1服" />
          
为对应服务器名称例如ServerName1对应为id 1	

请严格遵守GPL3.0许可协议
API调用缓存为1分钟

