# 数据库凭据泄露处理与 live 测试

## 当前代码的修复

已从当前文件树及本地 Git 历史中删除 `DatabaseManagementTests.cs`，并将 live 测试迁移到 `LiveDatabaseTests.cs`。该测试不含数据库地址、用户名或密码，也不读取应用的生产连接配置。

只有同时满足以下条件，xUnit 才会运行 live 测试，否则在发现测试时报告 Skip：

- `QCHA_RUN_LIVE_DB_TESTS` 严格等于 `1`。
- `QCHA_TEST_MYSQL_CONNECTION_STRING` 为非空的专用测试数据库连接字符串。

测试只查询 `PlayerData` 表，允许空表，查询取消时间为 15 秒。测试数据库需要包含项目使用的表结构；使用只读测试账号，不要使用生产数据库。连接字符串通过本机环境或 CI 的 Secret 注入，不要写入仓库、命令历史或测试输出。

在 `bot` 目录执行默认测试：

```powershell
dotnet test tests/Server_Qcha.Bot.Tests/Server_Qcha.Bot.Tests.csproj -c Release
```

显式执行 live 测试（先通过安全渠道注入专用连接字符串）：

```powershell
$env:QCHA_RUN_LIVE_DB_TESTS = '1'
try {
    dotnet test tests/Server_Qcha.Bot.Tests/Server_Qcha.Bot.Tests.csproj -c Release --filter 'Category=LiveDatabase'
} finally {
    Remove-Item Env:QCHA_RUN_LIVE_DB_TESTS -ErrorAction SilentlyContinue
    Remove-Item Env:QCHA_TEST_MYSQL_CONNECTION_STRING -ErrorAction SilentlyContinue
}
```

## 数据库管理员需要完成的操作

源码删除无法撤销已经泄露的密码。以下为泄露后的通用处理建议；本次部署按用户最新要求保留原密码，仅限制 MySQL 为本机访问（结果见下一节）：

1. 在管理端确认泄露用户名的全部 `user@host` 账号，立即轮换密码；如其他服务复用了该密码，也一并轮换。将新凭据通过 Secret 或被忽略的本地配置更新到合法客户端，确认旧密码已失效。
2. 在云安全组及主机防火墙将 MySQL 端口限制为已确认的服务端出口 IP、VPN 或私网来源，移除公网任意来源规则，同时检查 IPv6。不要将开发者动态公网地址当作长期访问策略。
3. 将数据库账号的 Host 限制到实际允许来源，处理允许任意来源的账号；为测试配置独立只读账号，仅授予测试表所需的 SELECT 权限。验证白名单来源可以访问、非白名单来源被拒绝。
4. 检查数据库登录/审计记录是否存在异常访问，必要时处理已建立的异常会话。

MySQL 账号由用户名和客户端 Host 共同定义，轮换时应确认正确的账号条目。参考官方文档：[账号与来源](https://dev.mysql.com/doc/refman/8.4/en/user-names.html)、[密码设置](https://dev.mysql.com/doc/refman/8.4/en/assigning-passwords.html)。具体操作以实际部署的 MySQL 版本为准。

## 本次服务器配置结果

经授权已通过远程管理在服务器备份并修改 `D:\mysql\my.ini`，在 `[mysqld]` 节设置：

```ini
bind-address=127.0.0.1
mysqlx-bind-address=127.0.0.1
```

MySQL 服务已重启。服务器上的监听检查确认普通协议 `3306` 和 X Protocol `33060` 均仅绑定 `127.0.0.1`，没有 IPv6 或非回环监听。本机使用原账号密码登录及查询 `PlayerData` 成功。配置备份为 `D:\mysql\my.ini.before-local-only-20260927-001042.bak`。

外部探测未收到数据库协议数据，读取超时；由于外部 TCP 建连仍被接受，不能把该探测描述为端口完全不可达。服务器未配置 Windows portproxy，监听地址检查是数据库只接受本机连接的主要验证依据。

服务器 Windows 防火墙配置文件均未启用，本次没有修改全局防火墙或云安全组。按用户要求未轮换密码；MySQL 账号的全局管理员权限也未改变。后续同机客户端应使用 `127.0.0.1`，远程直连不再由该 MySQL 实例接受。

## Git 历史

已确认提交 `8003a02` 引入了含凭据的测试。所有本地分支和标签的历史已重写，本地 reflog 和旧 Git 对象已清除；原 4 个本地提交得到保留。GitHub 远端历史尚未更新：清理分支已准备，但强制更新 `main`、`master` 和 `v1.4.0`、`v2.0.0` 标签的操作被安全审查拦截，需要仓库维护者批准后再推送。团队成员在推送完成后应重新克隆或按维护者指引迁移，避免旧提交重新进入远端。

