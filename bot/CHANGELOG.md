# Changelog

## v2.0.0 — 2026-09-21

### 🚀 新特性与架构升级
- **QQ 官方 Bot API 原生集成**：
  - 完整实现 QQ 开放平台 OpenAPI v2（基于 WebSocket 长连接、AccessToken 动态刷新与心跳保活机制）。
  - 支持群聊与单聊消息收发、被动回复配额管理（`msg_seq`）与长文本自动智能分段。
- **NapCat / QQ 官方 双接入热切换**：
  - 支持在 NapCat (OneBot 11) 与 QQ 官方 Bot API 之间在线切换，自动平滑迁移连接，无需重启进程。
- **QQ 官方指令面板（/v2/panels）自动创建与管理**：
  - 自动将 `BotCommandCatalog` 中的 11 条指令转换为腾讯官方规格（名称 ≤ 14 字符、介绍 ≤ 30 字符）。
  - 群聊场景自动为管理员命令（`/bc`、`/round`、`/ban`、`/setadmin`）打上 `only_admin: true` 权限标识。
  - Web 面板提供一键同步、实时拉取查看与面板删除功能。
- **现代化 Web 控制台（Qridge）**：
  - 基于 Vue 3 + Element Plus 的单页控制台，提供仪表盘、服务器管理、数据库查询、账户管理与审计日志。
  - 内置基于 SQLite 的轻量本地数据库（`panel.db`），支持 RBAC 权限体系与完整操作审计。
- **持久化数据集中隔离**：
  - 引入 `DataDirectoryManager`，所有本地运行时状态文件统一收敛至 `data/` 目录（`panel.db*`、`bot-settings.json`、`localadmin-servers.json`、`logging-level.json`），环境隔离与备份迁移更方便。
- **LocalAdmin 服务端进程托管**：
  - 支持直接由机器人进程监管并托管 LocalAdmin / SCPSL 游戏服务端实例。
- **玩家历史趋势采样与统计**：
  - 全自动周期性采样玩家在线数，支持当日峰值统计与可视化折线图。

---

## v1.0.0 — 2026-05-20
- 基础 OneBot 11 / NapCat 接入与 SCPSL 游戏服 TCP 命令交互。
- 基础命令：`/help`、`/cx`、`/info`、`/list`、`/bd`、`/me`、`/version`、`/bc`、`/round`、`/ban`、`/setadmin`。
