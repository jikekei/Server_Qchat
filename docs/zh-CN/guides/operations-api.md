# 运维 API 与日志归档

在 Web 面板「API 接口」中启用 API 并生成令牌。配置需要账号管理权限；只有令牌哈希写入 `data/operations-api.json`，原始令牌仅显示一次。重置令牌会立即撤销旧令牌，关闭 API 后所有令牌请求均被拒绝。启停操作另需打开「允许启动、停止和重启服务器」。控制操作与配置修改记录在审计日志中。建议通过 HTTPS 或可信网络访问。

请求头为 `Authorization: Bearer <令牌>`。接口前缀 `/api/ops/v1`。

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| GET | `/panel/logs` | 最近 2000 条面板运行日志（当前进程、受框架日志级别影响） |
| GET | `/servers` | 服务器状态列表 |
| GET | `/log-servers` | 托管服务器数量 `count` 和列表 `servers`，每项含 `id`、`name`、运行状态 |
| GET | `/servers/{id}/log-dates` | 所有有日志的日期及每日文件数、字节数；同时返回 `today` 和 `timeZone` |
| GET | `/servers/{id}/log-files?date=today` | 今天的全部日志文件；`date=yyyy-MM-dd` 选择指定日期，不传日期列出全部时间 |
| GET | `/servers/{id}` | 单服状态 |
| GET | `/servers/{id}/logs?after=0&limit=500` | 包含 stdout 的完整采集流；每次最多 2000 行 |
| GET | `/servers/{id}/archive` | 历史日志文件列表 |
| GET | `/servers/{id}/archive?file=相对文件名&offset=0` | 分页读取历史文件；将返回的 `next` 用于下一次 `offset`，直到 `more=false` |
| POST | `/servers/{id}/start` | 启动游戏服务器进程 |
| POST | `/servers/{id}/stop` | 优雅停止 |
| POST | `/servers/{id}/restart` | 优雅重启 |

`id` 是服务器进程实例 ID（例如 `main`）。启动、停止、重启返回 `success`、`message`、`error`；操作失败返回 HTTP 400，鉴权失败返回 401，控制权限关闭返回 403。启停响应表示指令已受理，完成情况请查询服务器状态。服务端异常可能返回 500。

日志选择流程：先请求 `/log-servers` 取得服务器数量和 ID，再请求该服务器的 `/log-dates` 取得全部日期，使用 `/log-files?date=today` 或指定日期列出文件，然后把文件列表返回的完整 `name` URL 编码后传入 `/archive?file=...&offset=0` 读取内容。日期使用 Bot 主机的本地时间；远程守护进程请保持同一时区。无日志的日期返回空文件列表；不存在的服务器返回 404，无效日期返回 400。`/archive` 也支持 `date` 参数。

实时日志来自内存环形缓冲，默认 2000 行；进程退出后该缓冲不保留。客户端使用最后一条返回日志的 `seq` 继续轮询，`truncated=true` 表示部分历史已超出缓冲，应改读归档。

游戏服务器文件日志始终保留全部采集内容，包括 stdout/stderr 和空行，不受实时控制台显示级别影响。日志目录默认相对负责托管游戏服的进程 ContentRoot，格式为：

```text
localadmin-logs/<实例ID>/<yyyy-MM>/<yyyy-MM-dd>/<HH-mm-ss-fffffff>-<随机标识>.txt
```

每天切换目录；每次托管进程重启或游戏服重新启动后新建 txt 文件。UTF-8 编码，追加时间、来源类型和原始内容。`WriteLogFiles=false` 可关闭文件写入；`LogExpirationDays=0` 保留全部文件，正数启用定期清理。旧的每日 `.log` 文件继续兼容查看与清理。独立守护模式需要同时更新 Bot 和 Daemon。

Web 面板「API 接口」页面也提供历史文件选择与分页阅读，文件内容始终按全部级别显示。原有服务器进程控制台仍遵守显示级别设置。
