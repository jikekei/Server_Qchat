import { useLocalAdmin } from '../modules/useLocalAdmin.js';
import { capabilities, can } from '../api.js';

export const LocalAdminView = {
  name: 'LocalAdminView',
  template: `
        <div>
          <el-alert v-if="!capabilities.localAdmin" type="warning" :closable="false" show-icon
                    title="LocalAdmin 网关未启用，服务器进程功能当前不可用" style="margin-bottom:12px">
            <div>请在机器人目录的 <code>appsettings.json</code> 中设置
              <code>"LocalAdmin": { "Enabled": true }</code>，然后重启机器人。</div>
            <div style="margin-top:6px">
              安全提示：该能力等同于服务器控制台，仅监听回环地址（只能管理本机进程），
              且需要 <code>server.control</code> 权限，所有操作都会写入审计日志。
            </div>
          </el-alert>

          <template v-if="capabilities.localAdmin">
          <!-- 守护进程 (Server_Qcha.Daemon) 监控与控制面板 -->
          <el-card shadow="hover" style="margin-bottom:14px; border-radius:8px">
            <template #header>
              <div style="display:flex; justify-content:space-between; align-items:center; flex-wrap:wrap; gap:8px">
                <div style="display:flex; align-items:center; gap:10px">
                  <span style="font-weight:bold; font-size:15px">🛡️ LocalAdmin 独立守护进程 (Server_Qcha.Daemon)</span>
                  <el-tag :type="daemon.online ? 'success' : 'danger'" effect="dark" size="small">
                    {{ daemon.online ? ('运行中 (PID: ' + (daemon.pid || '-') + ')') : '已停止 / 未运行' }}
                  </el-tag>
                </div>
                <div style="display:flex; align-items:center; gap:8px">
                  <el-button v-if="!daemon.online" type="success" size="small" :loading="daemon.loading" @click="startDaemon">
                    启动守护进程
                  </el-button>
                  <el-button v-if="daemon.online" type="warning" size="small" :loading="daemon.loading" @click="restartDaemon">
                    重启守护进程
                  </el-button>
                  <el-button v-if="daemon.online" type="danger" size="small" :loading="daemon.loading" @click="stopDaemon">
                    停止守护进程
                  </el-button>
                  <el-button size="small" :loading="daemon.loading" @click="fetchDaemonStatus">
                    刷新状态
                  </el-button>
                </div>
              </div>
            </template>
            <el-descriptions :column="4" border size="small">
              <el-descriptions-item label="物理内存 (工作集)">
                {{ daemon.online ? (daemon.memoryWorkingSetMb + ' MB') : '-' }}
              </el-descriptions-item>
              <el-descriptions-item label="专用内存">
                {{ daemon.online ? (daemon.memoryPrivateMb + ' MB') : '-' }}
              </el-descriptions-item>
              <el-descriptions-item label="运行时长">
                {{ daemon.online ? formatUptime(daemon.uptimeSeconds) : '-' }}
              </el-descriptions-item>
              <el-descriptions-item label="活动线程">
                {{ daemon.online ? daemon.threadCount : '-' }}
              </el-descriptions-item>
              <el-descriptions-item label="托管服务器">
                {{ daemon.online ? (daemon.runningServerCount + ' 运行中 / 共 ' + daemon.serverCount + ' 台') : '-' }}
              </el-descriptions-item>
              <el-descriptions-item label="内部通信端口">
                <code>{{ daemon.listenUri || 'http://127.0.0.1:10090' }}</code>
              </el-descriptions-item>
              <el-descriptions-item label="守护版本">
                {{ daemon.online ? daemon.version : '-' }}
              </el-descriptions-item>
              <el-descriptions-item label="状态说明">
                <span :style="{ color: daemon.online ? '#67C23A' : '#F56C6C', fontWeight: 500 }">
                  {{ daemon.message || (daemon.online ? '守护正常，关闭或升级面板游戏服不断线' : '守护进程未运行') }}
                </span>
              </el-descriptions-item>
            </el-descriptions>
          </el-card>

          <div class="toolbar">
            <el-select v-model="local.selectedId" style="width:260px" placeholder="选择服务器" @change="onLocalServerChange">
              <el-option v-for="s in local.servers" :key="s.id" :label="s.name" :value="s.id"></el-option>
            </el-select>
            <el-tag :type="localStatusType(local.status)" effect="dark">{{ localStatusText(local.status) }}</el-tag>
            <el-button :loading="local.loading" @click="refreshLocal">刷新</el-button>
            <el-button type="primary" @click="openServerDialog(null)">添加服务器</el-button>
            <el-button :disabled="!local.selectedId" @click="openServerDialog(local.selectedId)">编辑</el-button>
            <el-button type="danger" plain :disabled="!local.selectedId" @click="deleteServer">删除</el-button>
          </div>

          <p class="muted" style="margin:0 0 10px">
            配置来源：{{ local.source === 'config-file' ? '配置文件' : 'appsettings 种子' }}
            <template v-if="local.source === 'config-file'">（{{ local.configPath }}）</template>
            <template v-else>（在面板保存任意服务器后，会生成 localadmin-servers.json 并成为唯一来源）</template>
          </p>

          <div class="toolbar" style="margin-bottom:10px">
            <span class="muted">本地功能：</span>
            <el-button size="small" :loading="localConfigDialog.loading" @click="openLocalConfig">查看配置</el-button>
            <el-button size="small" :loading="local.resaving" @click="resaveLocalConfig">重写配置文件</el-button>
            <el-button size="small" @click="localHelpVisible = true">内置命令说明</el-button>
            <el-button size="small" @click="localLicenseVisible = true">许可与致谢</el-button>
          </div>

          <el-empty v-if="local.servers.length === 0" description="配置中没有托管任何本地服务器（LocalAdmin:Servers 为空）"></el-empty>

          <template v-else>
            <el-descriptions :column="4" border size="small">
              <el-descriptions-item label="进程 PID">{{ local.status && local.status.processId ? local.status.processId : '-' }}</el-descriptions-item>
              <el-descriptions-item label="运行时长">{{ formatUptime(local.status && local.status.uptimeSeconds) }}</el-descriptions-item>
              <el-descriptions-item label="游戏端口">{{ local.status ? local.status.gamePort : '-' }}</el-descriptions-item>
              <el-descriptions-item label="控制台端口">{{ local.status && local.status.consolePort ? local.status.consolePort : '-' }}</el-descriptions-item>
              <el-descriptions-item label="控制台连接">
                <el-tag size="small" :type="local.status && local.status.consoleConnected ? 'success' : 'info'">
                  {{ local.status && local.status.consoleConnected ? '已连接' : '未连接' }}
                </el-tag>
              </el-descriptions-item>
              <el-descriptions-item label="静默崩溃检测">{{ heartbeatText(local.status) }}</el-descriptions-item>
              <el-descriptions-item label="最近心跳">{{ lastHeartbeatText(local.status) }}</el-descriptions-item>
              <el-descriptions-item label="窗口内重启">
                {{ local.status ? (local.status.restartsInWindow + ' / ' + local.status.restartLimit) : '-' }}
              </el-descriptions-item>
            </el-descriptions>

            <div class="btn-row">
              <el-button type="success" :disabled="localRunning" @click="localStart">启动</el-button>
              <el-dropdown @command="localStop" :disabled="!localRunning">
                <el-button type="warning" :disabled="!localRunning">停止 ▾</el-button>
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item command="graceful">优雅停止（下发 exit 并等待）</el-dropdown-item>
                    <el-dropdown-item command="force">强制结束进程树</el-dropdown-item>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
              <el-button type="primary" :disabled="!localRunning" @click="localRestart(false)">重启</el-button>
              <el-button type="danger" :disabled="!localRunning" @click="localRestart(true)">强制重启</el-button>
              <el-button v-if="local.status && local.status.restartCountdownSeconds > 0" type="danger" @click="localCancelRestart">
                中止重启倒计时（{{ local.status.restartCountdownSeconds }}s）
              </el-button>
              <el-switch v-model="local.heartbeatSwitch"
                         :disabled="!(local.status && local.status.heartbeatEnabled)"
                         active-text="静默崩溃检测"
                         @change="localToggleHeartbeat"></el-switch>
              <span class="muted" style="margin-left:14px">控制台显示</span>
              <el-select v-model="local.consoleLevel" size="small" style="width:170px"
                         @change="localSetConsoleLevel">
                <el-option v-for="o in local.consoleLevels" :key="o.key" :label="o.label" :value="o.key"></el-option>
              </el-select>
              <span class="muted">（只影响之后的新输出）</span>
            </div>

            <el-alert v-if="local.status && local.status.restartBudgetExhausted" type="error" :closable="false" show-icon
                      title="自动重启已达限流上限，服务器不会自动拉起，请排查后手动启动"
                      style="margin-bottom:10px"></el-alert>
            <el-alert v-if="local.status && local.status.lastError" type="error" :closable="false" show-icon
                      :title="'最近一次错误：' + local.status.lastError" style="margin-bottom:10px"></el-alert>
            <el-alert v-if="local.truncated" type="warning" :closable="false" show-icon
                      title="控制台缓冲已滚动，更早的输出已被丢弃" style="margin-bottom:10px"></el-alert>

            <div class="terminal" ref="termRef">
              <div v-if="local.lines.length === 0" class="terminal-empty">（暂无输出）</div>
              <div v-for="line in local.lines" :key="line.seq" class="term-line">
                <span class="term-time">{{ timeOnly(line.time) }}</span>
                <span v-if="line.kind !== 'output'" class="term-badge">[{{ lineLabel(line.kind) }}]</span>
                <span :style="{ color: line.colorHex }">{{ line.text }}</span>
              </div>
            </div>

            <div class="console-input">
              <el-input v-model="local.command" placeholder="输入服务器控制台命令，回车发送"
                        @keyup.enter="localSendCommand"></el-input>
              <el-button type="primary" :loading="local.sending" @click="localSendCommand">发送</el-button>
              <el-checkbox v-model="local.autoScroll">自动滚动</el-checkbox>
              <el-button @click="localClearConsole">清空</el-button>
            </div>
            <p class="muted" style="margin-top:8px">
              内置命令：hbctrl enable|disable|status（崩溃检测开关）· hbc（中止重启倒计时）· restart · forcerestart · clear · help。
              其余输入原样转发给游戏服务端控制台执行。
            </p>
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
