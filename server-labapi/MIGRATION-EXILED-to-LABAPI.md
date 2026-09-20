# EXILED → LabAPI 迁移记录

| | 源 | 目标 |
|---|---|---|
| 目录 | `server/` | `server-labapi/` |
| 插件 API | EXILED 9.5.0 | LabAPI 1.1.7 |
| 目标框架 | .NET Framework 4.8.1 | .NET Framework 4.8（`net48`） |
| 构建方式 | 旧式 csproj，需 MSBuild / VS | SDK 风格 csproj，`dotnet build` 即可 |
| 协议 | —— | **完全兼容，机器人端无需改动** |

## 0. 为什么这次迁移风险不高

LabAPI 是 Northwood 官方维护的 EXILED 继任者，设计上刻意沿用了大量 EXILED 命名。
5 个源文件里有 2 个（`TcpCommandServer.cs`、`BotNotificationClient.cs`）**只删了一行 `using` 别名**，
真正的 API 替换集中在 `Main.cs` / `CommandDispatcher.cs` / `AcCommand.cs`。

## 1. API 映射对照表

| 用途 | EXILED 9.5.0 | LabAPI 1.1.7 | 备注 |
|---|---|---|---|
| 插件基类 | `Exiled.API.Features.Plugin<Config>` | `LabApi.Loader.Features.Plugins.Plugin<Config>` | 结构基本一致 |
| 启用 / 禁用回调 | `OnEnabled()` / `OnDisabled()` | `Enable()` / `Disable()` | 抽象方法，**不调用 `base`** |
| 配置接口 | `Exiled.API.Interfaces.IConfig` | **没有** | 配置变普通 POCO，约束 `TConfig : class, new()` |
| 插件启用开关 | 配置类里的 `IsEnabled` | `properties.yml` 的 `is_enabled` | LabAPI 保留文件，不要写进 Config |
| 配置文件名 | 自动 | `Plugin<T>.ConfigFileName`，默认 `config.yml` | |
| 配置目录 | `EXILED/Configs/<端口>-<插件名>.yml` | `LabAPI/configs/<端口>/<插件名>/config.yml` | 目录名 = `Plugin.Name` |
| 配置命名规则 | 下划线 | 下划线（`UnderscoredNamingConvention`） | **键名不变，旧配置可直接复用** |
| 配置注释 | `[Description]` | `[Description]` | 序列化器行为一致 |
| 配置生成 | 自动 | 自动 | 文件不存在时创建默认值，存在时读入并回写补齐新字段 |
| 日志 | `Exiled.API.Features.Log` | `LabApi.Features.Console.Logger` | `Debug(msg)` → `Debug(msg, bool canBePrinted)` |
| 事件注册 | `Exiled.Events.Handlers.Server.WaitingForPlayers` | `LabApi.Events.Handlers.ServerEvents.WaitingForPlayers` | 均为无参委托，签名相同 |
| 玩家集合 | `Player.List` | `Player.List` | `IReadOnlyCollection<Player>` |
| 按角色取玩家 | `Player.Get(RoleTypeId)` | 无此重载 | 改 `Player.List.Where(p => p.Role == role)` |
| 按队伍取玩家 | `Player.Get(Team)` | 无此重载 | 改 `Player.List.Where(p => p.Team == team)` |
| 由指令发送者取玩家 | `Player.Get(sender)` | `Player.Get(ICommandSender)` | 一致 |
| 玩家数字 ID | `player.Id` | `player.PlayerId` | |
| 是否管理员 | `player.RemoteAdminAccess` | `player.RemoteAdminAccess` | 一致 |
| 封禁 | `player.Ban(duration, reason)` | `player.Ban(reason, duration)` | ⚠️ **参数顺序相反** |
| 单人广播 | `player.Broadcast(10, text)` | `player.SendBroadcast(text, 10, Broadcast.BroadcastFlags.Normal, false)` | 参数顺序变，且多了 flags / 是否清空 |
| 全服广播 | `Map.Broadcast(15, text)` | `Server.SendBroadcast(text, 15, Broadcast.BroadcastFlags.Normal, false)` | |
| 最大人数 | `Server.MaxPlayerCount` | `Server.MaxPlayers` | |
| 服务器端口 | `Server.Port`（`int`） | `Server.Port`（`ushort`） | |
| 重启服务器 | `Server.Restart()` | `Server.Restart()` | 一致 |
| 回合是否已开始 | `Round.IsStarted` | `Round.IsRoundStarted` | |
| 回合已进行时长 | `Round.ElapsedTime`（`TimeSpan`） | `Round.Duration`（`TimeSpan`） | |
| 回合计数 | `Round.UptimeRounds` | `RoundRestarting.RoundRestart.UptimeRounds` | LabAPI 无包装器，直接用游戏静态字段 |
| 开始回合 | `Round.Start()` | `Round.Start()` | 一致 |
| 重启回合 | `Round.Restart(bool fastRestart)` | `Round.Restart(bool fastRestart, bool overrideRestartAction, NextRoundAction)` | 见下方行为差异 ② |
| 大厅锁 | `Round.IsLobbyLocked` | `Round.IsLobbyLocked` | 一致 |
| 下一波刷新时间 | `Respawn.ProtectionTime` | `RespawnWaves.PrimaryMtfWave.TimeLeft` / `PrimaryChaosWave.TimeLeft` | 取两者较小值 |
| 指令接口 | `CommandSystem.ICommand` | `CommandSystem.ICommand` | 一致 |
| 指令注册 | `[CommandHandler(typeof(ClientCommandHandler))]` | 同 | LabAPI 装载时自动扫描插件程序集注册 |

## 2. 行为差异（需要留意的 4 处）

### ① `/info` 的两处输出格式变化

- 「回合进行时间」：`TimeSpan` 默认输出（`00:05:23.1234567`）改为 `hh:mm:ss`。
- 「下一波刷新时间」：由 EXILED 的 `Respawn.ProtectionTime` 改为「九尾狐 / 混沌两条刷新波中
  更早到来的那一个」，单位秒，保留 1 位小数。

机器人端只是转发文本、不做解析，不影响协议。若下游有文本断言需要同步调整。

### ② `/rest` 的等价写法

EXILED 的 `Round.Restart(false)` 只有一个参数；LabAPI 拆成了三个。迁移取：

```csharp
Round.Restart(false, false, ServerStatic.NextRoundAction.DoNothing);
//              ↑      ↑      ↑
//       非快速重启  不覆盖  覆盖时才生效的动作
```

`overrideRestartAction = false` 表示沿用服务器自身配置的回合结束动作，语义上等价于旧行为。
原有的「回合开始超过 60 秒则拒绝」守卫逻辑保留未动。

### ③ 日志 Debug 开关

EXILED 的 `Log.Debug` 是全局行为；LabAPI 的 `Logger.Debug(object, bool canBePrinted)` 需要显式传开关。
迁移新增了 `Log.cs` 门面类（命名空间内同名类，替代原来的 `using Log = Exiled.API.Features.Log;`），
把 `Log.Debug(...)` 统一绑定到 `config.yml` 的 `debug`，业务代码里的日志调用一行没改。

### ④ `RequiredApiVersion` 的写法（有踩坑）

LabAPI 官方示例写的是 `new Version(LabApiProperties.CompiledVersion)`，但实测：

```
AssemblyFileVersionAttribute        = 1.1.7.0
AssemblyInformationalVersionAttribute = 1.1.7+84b0da472e3ce42bf86cb49bde8379d00605a8f6
CompiledVersion / CurrentVersion    = 运行时 static 字段（非编译期常量）
```

`CompiledVersion` 是**字符串**，而 LabAPI 的程序集信息版本带 `+<git-sha>` 后缀。
一旦该字符串格式变化，`new Version(...)` 就会抛异常 —— 而 `PluginLoader.ValidateVersion`
的调用点**没有 try/catch**，异常会往上冒并中断整个 LabAPI 初始化。

因此迁移改用：

```csharp
public override Version RequiredApiVersion => LabApiProperties.CurrentVersion;
```

`CurrentVersion` 本身就是 `Version` 对象，无解析步骤、无副作用，形为上也与官方示例等价。

## 3. 刻意没有改动的东西

- **TCP 协议、端口分配、AuthToken 双向鉴权、半关闭定界、心跳指数退避**：一行未改。
- **`.ac` 客户端指令**：逻辑与回显一致，仅把广播调用换成 LabAPI 的形式。
- **旧 EXILED 版 `server/`**：完整保留、未删除，可随时回滚。
- 交接文档里记录的既有技术债（`PlayerRepository` 无建表脚本、`/setadmin` 未真写权限、
  测试覆盖近零）**依然存在**，与本次迁移无关。

## 4. 验证状态

- [x] 编译通过：**0 警告 0 错误**
- [x] 产物 `bin/Release/Server_Qcha.dll` 为 35 KB 单文件，不携带依赖副本
- [x] 产物零 EXILED 残留引用；运行时仅依赖 `LabApi` / `Assembly-CSharp` / `CommandSystem.Core`，
      三者均由游戏进程提供
- [x] 依赖 DLL 随仓库内置于 `Refs/`，符合仓库「不引用本地 Steam 路径」的约定
- [x] `RoleTypeId` / `Team` 枚举成员逐一对照游戏程序集核验，无成员被移除
- [ ] **未做运行时联调**：需在测试端口实际起服，验证注册 → 心跳 → 指令 → `.ac` 全链路
- [ ] 未验证与机器人端的 Token 组合下命令通道鉴权是否正常

## 5. 回滚

`plugins/<端口>/Server_Qcha.dll` 换回 EXILED 版构建产物，并切回 EXILED 加载器即可。
两版配置键名一致，`config.yml` 可直接复用。
