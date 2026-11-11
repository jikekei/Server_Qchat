import { useLocalAdmin } from '../modules/useLocalAdmin.js';
import { capabilities, can } from '../api.js';

export const LocalAdminView = {
  name: 'LocalAdminView',
  template: `
        <div>
          <div class="page-header">
            <div class="page-header-info">
              <h2>服务器进程 (LocalAdmin)</h2>
              <p>独立守护进程监控、SCPSL 实例生命周期管控、SteamCMD 不停服更新与实时交互终端</p>
            </div>
            <div class="page-header-actions" v-if="capabilities.localAdmin">
              <el-button size="small" :loading="localConfigDialog.loading" @click="openLocalConfig">查看配置 (lacfg)</el-button>
              <el-button size="small" :loading="local.resaving" @click="resaveLocalConfig">重写配置文件</el-button>
              <el-button size="small" @click="localHelpVisible = true">命令参考</el-button>
              <el-button size="small" @click="localLicenseVisible = true">开源许可</el-button>
              <el-button size="small" type="primary" plain :loading="local.loading" @click="refreshLocal">刷新全部</el-button>
            </div>
          </div>

          <el-alert v-if="!capabilities.localAdmin" type="warning" :closable="false" show-icon
                    title="LocalAdmin 网关未启用，服务器进程功能当前不可用" style="margin-bottom:16px">
            <div>请在机器人目录的 <code>appsettings.json</code> 中设置
              <code>"LocalAdmin": { "Enabled": true }</code>，然后重启机器人。</div>
            <div style="margin-top:6px">
              安全提示：该能力等同于服务器控制台，仅监听回环地址（只能管理本机进程），
              且需要 <code>server.control</code> 权限，所有操作都会写入审计日志。
            </div>
          </el-alert>

          <template v-if="capabilities.localAdmin">
            <!-- 守护进程 (Server_Qcha.Daemon) 监控与控制面板 -->
            <el-card shadow="never" style="margin-bottom:18px">
              <template #header>
                <div class="section-card-head">
                  <div class="section-card-title">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                      <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path>
                    </svg>
                    <span>LocalAdmin 独立守护进程 (Server_Qcha.Daemon)</span>
                    <el-tag :type="daemon.online ? 'success' : 'danger'" effect="dark" size="small">
                      {{ daemon.online ? ('运行中 · PID ' + (daemon.pid || '-')) : '已停止 / 未运行' }}
                    </el-tag>
                    <el-tag v-if="daemon.online && daemon.version" type="info" size="small" effect="plain">
                      v{{ daemon.version }}
                    </el-tag>
                  </div>
                  <div style="display:flex; align-items:center; gap:8px; flex-wrap:wrap">
                    <el-button v-if="!daemon.online" type="success" size="small" :loading="daemon.loading" @click="startDaemon">
                      ▶ 启动守护进程
                    </el-button>
                    <el-button v-if="daemon.online" type="warning" plain size="small" :loading="daemon.loading" @click="restartDaemon">
                      ↻ 重启守护进程
                    </el-button>
                    <el-button v-if="daemon.online" type="danger" plain size="small" :loading="daemon.loading" @click="stopDaemon">
                      ⏹ 停止守护进程
                    </el-button>
                    <el-button size="small" :loading="daemon.loading" @click="fetchDaemonStatus">
                      刷新状态
                    </el-button>
                  </div>
                </div>
              </template>

              <div class="metric-tiles">
                <div class="metric-tile">
                  <div class="metric-tile-label">物理内存 / 专用内存</div>
                  <div class="metric-tile-value">{{ daemon.online ? (daemon.memoryWorkingSetMb + ' MB') : '—' }}</div>
                  <div class="metric-tile-sub">专用分配：{{ daemon.online ? (daemon.memoryPrivateMb + ' MB') : '—' }}</div>
                </div>
                <div class="metric-tile">
                  <div class="metric-tile-label">守护进程存活时长</div>
                  <div class="metric-tile-value">{{ daemon.online ? formatUptime(daemon.uptimeSeconds) : '—' }}</div>
                  <div class="metric-tile-sub">活动并发线程：{{ daemon.online ? daemon.threadCount : '—' }}</div>
                </div>
                <div class="metric-tile">
                  <div class="metric-tile-label">托管服务器容量</div>
                  <div class="metric-tile-value">{{ daemon.online ? (daemon.runningServerCount + ' / ' + daemon.serverCount + ' 台') : '—' }}</div>
                  <div class="metric-tile-sub">运行中实例 / 已配置总数</div>
                </div>
                <div class="metric-tile">
                  <div class="metric-tile-label">内部通信与防掉线状态</div>
                  <div class="metric-tile-value" style="font-size:13px">{{ daemon.listenUri || 'http://127.0.0.1:10090' }}</div>
                  <div class="metric-tile-sub" :style="{ color: daemon.online ? 'var(--qb-success)' : 'var(--qb-danger)' }">
                    {{ daemon.message || (daemon.online ? '守护正常，重启面板玩家不掉线' : '守护进程未运行') }}
                  </div>
                </div>
              </div>
            </el-card>

            <!-- 实例切换与配置工具栏 -->
            <div class="toolbar-card">
              <span style="font-weight:600;font-size:13px">当前实例：</span>
              <el-select v-model="local.selectedId" style="width:260px" placeholder="选择托管服务器" @change="onLocalServerChange">
                <el-option v-for="s in local.servers" :key="s.id" :label="s.name" :value="s.id"></el-option>
              </el-select>
              <el-tag :type="localStatusType(local.status)" effect="dark">{{ localStatusText(local.status) }}</el-tag>

              <span class="muted">
                来源：{{ local.source === 'config-file' ? '独立配置文件' : 'appsettings 种子' }}
                <template v-if="local.source === 'config-file'"> (<code>{{ local.configPath }}</code>)</template>
              </span>

              <div style="margin-left:auto;display:flex;gap:8px;flex-wrap:wrap">
                <el-button type="primary" @click="openServerDialog(null)">+ 添加服务器</el-button>
                <el-button :disabled="!local.selectedId" @click="openServerDialog(local.selectedId)">编辑配置</el-button>
                <el-button type="danger" plain :disabled="!local.selectedId" @click="deleteServer">删除实例</el-button>
              </div>
            </div>

            <el-empty v-if="local.servers.length === 0" description="配置中没有托管任何本地服务器（点击右上角「+ 添加服务器」开始托管）"></el-empty>

            <template v-else>
              <!-- 选中实例核心指标瓷砖 -->
              <div class="metric-tiles" style="margin-bottom:14px">
                <div class="metric-tile">
                  <div class="metric-tile-label">实例进程 PID & 存活时长</div>
                  <div class="metric-tile-value">{{ local.status && local.status.processId ? ('PID ' + local.status.processId) : '未运行' }}</div>
                  <div class="metric-tile-sub">运行时长：{{ formatUptime(local.status && local.status.uptimeSeconds) }}</div>
                </div>
                <div class="metric-tile">
                  <div class="metric-tile-label">网络与管道端口</div>
                  <div class="metric-tile-value">游戏 {{ local.status ? local.status.gamePort : '-' }}</div>
                  <div class="metric-tile-sub">控制台端口：{{ local.status && local.status.consolePort ? local.status.consolePort : '-' }}</div>
                </div>
                <div class="metric-tile">
                  <div class="metric-tile-label">控制台管道 & 心跳检测</div>
                  <div class="metric-tile-value">
                    <el-tag size="small" :type="local.status && local.status.consoleConnected ? 'success' : 'info'">
                      {{ local.status && local.status.consoleConnected ? '管道已连接' : '管道未连接' }}
                    </el-tag>
                  </div>
                  <div class="metric-tile-sub">状态机：{{ heartbeatText(local.status) }}</div>
                </div>
                <div class="metric-tile">
                  <div class="metric-tile-label">最近心跳 & 防崩限流窗口</div>
                  <div class="metric-tile-value">{{ lastHeartbeatText(local.status) }}</div>
                  <div class="metric-tile-sub">窗口内重启：{{ local.status ? (local.status.restartsInWindow + ' / ' + local.status.restartLimit + ' 次') : '-' }}</div>
                </div>
              </div>

              <!-- 生命周期与操作控制栏 -->
              <div class="toolbar-card" style="padding:10px 16px;margin-bottom:14px">
                <el-button type="success" :disabled="localRunning || localUpdating" @click="localStart">▶ 启动实例</el-button>
                <el-dropdown @command="localStop" :disabled="!localRunning">
                  <el-button type="warning" :disabled="!localRunning">⏹ 停止实例 ▾</el-button>
                  <template #dropdown>
                    <el-dropdown-menu>
                      <el-dropdown-item command="graceful">优雅停止（下发 exit 并等待）</el-dropdown-item>
                      <el-dropdown-item command="force">强制结束进程树</el-dropdown-item>
                    </el-dropdown-menu>
                  </template>
                </el-dropdown>
                <el-button type="primary" :disabled="!localRunning || localUpdating" @click="localRestart(false)">↻ 平滑重启</el-button>
                <el-button type="danger" plain :disabled="!localRunning || localUpdating" @click="localRestart(true)">⚡ 强制重启</el-button>
                <el-button type="warning" plain :disabled="!local.selectedId" @click="openUpdateDialog">
                  {{ localUpdating ? '⏳ 查看更新进度' : '⬆ 更新服务器 (SteamCMD)' }}
                </el-button>
                <el-button v-if="local.status && local.status.restartCountdownSeconds > 0" type="danger" @click="localCancelRestart">
                  中止重启倒计时 ({{ local.status.restartCountdownSeconds }}s)
                </el-button>

                <div style="margin-left:auto;display:flex;align-items:center;gap:14px;flex-wrap:wrap">
                  <el-switch v-model="local.heartbeatSwitch"
                             :disabled="!(local.status && local.status.heartbeatEnabled)"
                             active-text="静默崩溃检测"
                             @change="localToggleHeartbeat"></el-switch>
                </div>
              </div>

              <el-alert v-if="localUpdating" type="warning" :closable="false" show-icon
                        :title="local.status && local.status.running
                          ? '服务端更新进行中：新版本在暂存目录下载，服务器照常运行，当前回合结束后自动切换（可在「查看更新进度」里立即更新或取消）'
                          : '正在通过 SteamCMD 更新服务端文件，期间无法启动 / 重启 / 修改配置，进度见「查看更新进度」或下方控制台'"
                        style="margin-bottom:12px"></el-alert>
              <el-alert v-if="local.status && local.status.restartBudgetExhausted" type="error" :closable="false" show-icon
                        title="自动重启已达限流上限，服务器不会自动拉起，请排查故障后手动启动"
                        style="margin-bottom:12px"></el-alert>
              <el-alert v-if="local.status && local.status.lastError" type="error" :closable="false" show-icon
                        :title="'最近一次异常：' + local.status.lastError" style="margin-bottom:12px"></el-alert>
              <el-alert v-if="local.truncated" type="warning" :closable="false" show-icon
                        title="控制台环形缓冲区已滚动，更早的输出已被归档" style="margin-bottom:12px"></el-alert>

              <!-- 专业终端窗口 -->
              <div class="terminal-window">
                <div class="terminal-header">
                  <div class="terminal-header-left">
                    <div class="terminal-dots">
                      <span class="terminal-dot red"></span>
                      <span class="terminal-dot yellow"></span>
                      <span class="terminal-dot green"></span>
                    </div>
                    <span class="terminal-title">
                      {{ (local.status && local.status.name) || local.selectedId || 'SCPSL Console' }}
                    </span>
                    <span style="color:#64748b">({{ local.lines.length }} 行缓冲)</span>
                  </div>
                  <div class="terminal-header-right">
                    <span style="color:#94a3b8">输出过滤：</span>
                    <el-select v-model="local.consoleLevel" size="small" style="width:155px"
                               @change="localSetConsoleLevel">
                      <el-option v-for="o in local.consoleLevels" :key="o.key" :label="o.label" :value="o.key"></el-option>
                    </el-select>
                    <el-checkbox v-model="local.autoScroll" style="color:#cbd5e1">自动滚动</el-checkbox>
                    <el-button size="small" @click="localClearConsole">清空屏幕</el-button>
                  </div>
                </div>

                <div class="terminal" ref="termRef">
                  <div v-if="local.lines.length === 0" class="terminal-empty">（终端就绪，等待服务器控制台输出…）</div>
                  <div v-for="line in local.lines" :key="line.seq" class="term-line">
                    <span class="term-time">{{ timeOnly(line.time) }}</span>
                    <span v-if="line.kind !== 'output'" class="term-badge">[{{ lineLabel(line.kind) }}]</span>
                    <span :style="{ color: line.colorHex }">{{ line.text }}</span>
                  </div>
                </div>

                <div class="terminal-footer">
                  <div class="console-input">
                    <el-input v-model="local.command" placeholder="输入 SCPSL 服务端控制台指令或 LocalAdmin 内置命令，按 Enter 发送…"
                              @keyup.enter="localSendCommand">
                      <template #prefix>
                        <span style="font-family:var(--qb-mono);color:var(--qb-accent);font-weight:700">&gt;</span>
                      </template>
                    </el-input>
                    <el-button type="primary" :loading="local.sending" @click="localSendCommand">发送指令</el-button>
                  </div>
                  <div class="quick-cmd-bar">
                    <span class="muted">快捷指令：</span>
                    <span class="quick-cmd-chip" @click="local.command = 'help'; localSendCommand()">help</span>
                    <span class="quick-cmd-chip" @click="local.command = 'hbctrl status'; localSendCommand()">hbctrl status</span>
                    <span class="quick-cmd-chip" @click="local.command = 'players'; localSendCommand()">players</span>
                    <span class="quick-cmd-chip" @click="local.command = 'roundrestart'; localSendCommand()">roundrestart</span>
                    <span class="quick-cmd-chip" @click="local.command = 'forcestart'; localSendCommand()">forcestart</span>
                    <span class="quick-cmd-chip" @click="local.command = 'clear'; localSendCommand()">clear</span>
                  </div>
                </div>
              </div>
            </template>
          </template><!-- /capabilities.localAdmin -->
        </div>
`,
  setup() {
    const localAdmin = useLocalAdmin();
    return {
      ...localAdmin,
      capabilities,
      can,
    };
  }
};
