# LocalAdmin 面板冒烟测试

端到端验证机器人的 **LocalAdmin 能力**（在 Web 面板里托管 SCPSL 服务端进程）。

核心思路：不去启动真实的游戏服务端，而是用一个**会说官方控制台协议的假游戏进程**
（`FakeGame/`）冒名顶替 `SCPSL.exe`。它如实复刻了 `ServerOutput.TcpConsole` 的上行帧格式、
下行命令读取、控制码与退出动作声明，因此可以在几秒钟内把整条链路跑通并断言。

## 运行

```bash
bash bot/tests/LocalAdmin.SmokeTest/run-smoke.sh
```

脚本会依次：构建 FakeGame → 构建机器人 → 在 `%TEMP%\localadmin-smoke\` 下准备隔离的
账号库与日志目录 → 以测试配置启动机器人 → 跑断言 → 结束时清理机器人进程。

退出码 0 表示全部通过。

可选环境变量：`SMOKE_PORT`（默认 8099）、`SMOKE_WORK`（暂存目录）、`DOTNET`、`PYTHON`。

## 覆盖范围

| 分组 | 断言内容 |
|---|---|
| 能力探针 | `capabilities.localAdminGateway` / 托管实例列表 / 初始状态 |
| 上行帧解析 | 颜色码 15/11/14/12 → 对应的十六进制色、多行负载拆行、控制码 0x10、stdout 重定向 |
| 下行帧往返 | 4 字节小端长度前缀 + UTF-8 正文被游戏侧正确解出并回显 |
| 心跳状态机 | 首个心跳后 `awaiting → active`、`hbctrl` 开关、`hbctrl status` |
| 退出动作协商 | 0x13 崩溃 → 自动重启且**计入**限流；面板主动重启**不计入** |
| 误判防护 | 用户下发 `exit` 主动关服 → 不触发自动重启 |
| 崩溃循环保护 | 重启次数达上限后置位 `restartBudgetExhausted` 并停止拉起 |
| 优雅停止 / 审计 | `force=false` 走 exit 协商；`localadmin.*` 审计记录齐备 |

## 两个容易踩到的坑（脚本里已处理）

1. **MSBuild 无法解析含非 ASCII 字符的绝对路径参数**（会报「响应文件追加的开关 …」）。
   因此构建一律用相对路径，运行期产物复制到 `%TEMP%` 下的纯 ASCII 目录。
2. **`.NET` 控制台重定向到文件时用的是系统 ANSI 代码页（中文 Windows 为 GBK），不是 UTF-8**。
   所以断言脚本在**字节层面**做纯 ASCII 匹配来解析启动公告板里的随机密码，不受日志编码影响。

另外，从 Git Bash 调用原生 `python.exe` 时必须传 **Windows 风格路径**（`pwd -W`），
传 `/c/...` 会被解释成 `C:\c\...` 而找不到文件。

## 注意

- 本测试不读写生产配置，也不触碰真实游戏服务端的任何数据；使用独立的端口（面板 8099、
  通知通道 10098）与独立的 `panel-smoke.db`。
- `FakeGame` 是测试脚手架，**不在** `Server_Qcha.Bot.sln` 内，CI 不会构建它。
