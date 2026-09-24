import { useOverview } from '../modules/useOverview.js';
import { useServers } from '../modules/useServers.js';
import { can, fmtTime } from '../api.js';

export const OverviewView = {
  name: 'OverviewView',
  template: `
        <div>
          <div class="toolbar">
            <el-button :loading="overview.loading" @click="loadOverview">刷新数据</el-button>
            <span class="muted">最后更新：{{ overview.lastUpdated ? fmtTime(overview.lastUpdated) : '-' }}</span>
            <span class="muted" style="margin-left:auto">数据每 30 秒自动采集 · 页面每 15 秒自动同步</span>
          </div>

          <!-- KPI 指标卡片 -->
          <div class="overview-grid">
            <el-card class="kpi-card" shadow="hover">
              <div class="kpi-info">
                <span class="kpi-title">托管服务器</span>
                <span class="kpi-value">{{ overview.stats.serverCount }} <span style="font-size:14px;font-weight:400;color:var(--qb-text-2)">台</span></span>
                <span class="kpi-sub">
                  <span class="pulse-dot" :class="{ offline: overview.stats.serverCount === 0 || (overview.stats.localAdminEnabled && overview.stats.localRunningCount === 0 && overview.stats.onlineServers === 0) }"></span>
                  <template v-if="overview.stats.localAdminEnabled">
                    {{ overview.stats.localRunningCount > 0 ? (overview.stats.localRunningCount + ' 台运行中') : (overview.stats.serverCount > 0 ? '已配置待启动' : '等待添加服务器') }}
                  </template>
                  <template v-else>
                    {{ overview.stats.onlineServers > 0 ? (overview.stats.onlineServers + ' 台在线') : (overview.stats.serverCount > 0 ? '网关运行中' : '等待服务器注册') }}
                  </template>
                </span>
              </div>
              <div class="kpi-icon-box kpi-icon-servers">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="2" y="2" width="20" height="8" rx="2" ry="2"></rect>
                  <rect x="2" y="14" width="20" height="8" rx="2" ry="2"></rect>
                  <line x1="6" y1="6" x2="6.01" y2="6"></line>
                  <line x1="6" y1="18" x2="6.01" y2="18"></line>
                </svg>
              </div>
            </el-card>

            <el-card class="kpi-card" shadow="hover">
              <div class="kpi-info">
                <span class="kpi-title">全服实时玩家</span>
                <span class="kpi-value">{{ overview.stats.totalOnline }} <span style="font-size:14px;font-weight:400;color:var(--qb-text-2)">/ {{ overview.stats.totalMax }}</span></span>
                <span class="kpi-sub">
                  <span style="color:var(--qb-accent);font-weight:600">今日峰值 {{ overview.stats.peakToday }} 人</span>
                </span>
              </div>
              <div class="kpi-icon-box kpi-icon-players">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path>
                  <circle cx="9" cy="7" r="4"></circle>
                  <path d="M23 21v-2a4 4 0 0 0-3-3.87"></path>
                  <path d="M16 3.13a4 4 0 0 1 0 7.75"></path>
                </svg>
              </div>
            </el-card>

            <el-card class="kpi-card" shadow="hover">
              <div class="kpi-info">
                <span class="kpi-title">QQ 机器人</span>
                <span class="kpi-value" :style="{ color: overview.stats.botConnected ? 'var(--qb-text)' : '#ef4444' }">
                  {{ overview.stats.botConnected ? '已连接' : '未连接' }}
                </span>
                <span class="kpi-sub">
                  <span class="pulse-dot" :class="{ offline: !overview.stats.botConnected }"></span>
                  <span v-if="overview.stats.botConnected">
                    {{ overview.stats.botNickname ? (overview.stats.botNickname + ' 在线') : '在线' }}
                  </span>
                  <span v-else>
                    {{ overview.stats.botPlatform === 'OfficialQq' ? '官方 API 待连接' : 'NapCat 离线待连' }}
                  </span>
                </span>
              </div>
              <div class="kpi-icon-box kpi-icon-bot">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="3" y="11" width="18" height="10" rx="2"></rect>
                  <circle cx="12" cy="5" r="2"></circle>
                  <path d="M12 7v4"></path>
                  <line x1="8" y1="16" x2="8.01" y2="16"></line>
                  <line x1="16" y1="16" x2="16.01" y2="16"></line>
                </svg>
              </div>
            </el-card>

            <el-card class="kpi-card" shadow="hover">
              <div class="kpi-info">
                <span class="kpi-title">MySQL 数据库</span>
                <span class="kpi-value" :style="{ color: overview.stats.mysqlConnected ? 'var(--qb-text)' : '#ef4444' }">
                  {{ overview.stats.mysqlConnected ? '正常' : '未连接' }}
                </span>
                <span class="kpi-sub">
                  <span class="pulse-dot" :class="{ offline: !overview.stats.mysqlConnected }"></span>
                  {{ overview.stats.mysqlConnected ? '数据持久化正常' : (overview.stats.dbConfigured ? '连接异常请排查' : '请检查数据库配置') }}
                </span>
              </div>
              <div class="kpi-icon-box kpi-icon-mysql">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <ellipse cx="12" cy="5" rx="9" ry="3"></ellipse>
                  <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"></path>
                  <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"></path>
                </svg>
              </div>
            </el-card>
          </div>

          <!-- 游戏服务器综合负载与运行健康监控面板 -->
          <el-card class="chart-card server-load-card" shadow="hover">
            <template #header>
              <div class="chart-title">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="color:var(--qb-accent)">
                  <path d="M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z"></path>
                </svg>
                <span>游戏服务器综合负载监控</span>
                <el-tag size="small" :type="getStatusTagType(overview.serverStatus.status)" effect="dark" style="margin-left:8px;font-weight:600">
                  {{ overview.serverStatus.statusText }} ({{ overview.serverStatus.status }})
                </el-tag>
              </div>
              <div class="server-load-meta muted">
                <span>平滑负载：<b :style="{ color: getStatusColor(overview.serverStatus.status) }">{{ overview.serverStatus.smoothLoad }}%</b></span>
                <span class="meta-sep">·</span>
                <span>60s 峰值：<b style="color:var(--qb-text)">{{ overview.serverStatus.peakLoad }}%</b></span>
                <span class="meta-sep">·</span>
                <span>加权模型与峰值保护计算</span>
              </div>
            </template>

            <div class="load-dashboard-grid">
              <!-- 左侧：综合负载环形仪表与瓶颈诊断 -->
              <div class="load-gauge-col">
                <div class="load-gauge-box">
                  <div class="load-gauge-circle" :style="{ borderColor: getStatusColor(overview.serverStatus.status) }">
                    <span class="load-gauge-number" :style="{ color: getStatusColor(overview.serverStatus.status) }">
                      {{ overview.serverStatus.load }}<small>%</small>
                    </span>
                    <span class="load-gauge-label">综合负载</span>
                  </div>
                  <div class="load-bottleneck-summary">
                    <div class="bottleneck-row">
                      <span class="bottleneck-tag primary">主要瓶颈</span>
                      <span class="bottleneck-name">{{ overview.serverStatus.primaryBottleneck || '暂无瓶颈' }}</span>
                    </div>
                    <div class="bottleneck-row">
                      <span class="bottleneck-tag secondary">次要瓶颈</span>
                      <span class="bottleneck-name">{{ overview.serverStatus.secondaryBottleneck || '暂无瓶颈' }}</span>
                    </div>
                  </div>
                </div>

                <div class="load-diagnosis-card">
                  <div class="diagnosis-header">
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="color:var(--qb-accent)">
                      <circle cx="12" cy="12" r="10"></circle>
                      <line x1="12" y1="16" x2="12" y2="12"></line>
                      <line x1="12" y1="8" x2="12.01" y2="8"></line>
                    </svg>
                    <span>智能诊断建议</span>
                  </div>
                  <div class="diagnosis-body">
                    {{ overview.serverStatus.diagnosis }}
                  </div>
                </div>
              </div>

              <!-- 右侧：六维指标压力分布条 -->
              <div class="load-metrics-col">
                <div class="metrics-grid">
                  <!-- 1. 游戏主线程压力 (权重 25%) -->
                  <div class="metric-item">
                    <div class="metric-header">
                      <span class="metric-name">游戏主线程压力</span>
                      <span class="metric-weight">权重 25%</span>
                      <span class="metric-val" :style="{ color: getPressureColor(overview.serverStatus.mainThread) }">{{ overview.serverStatus.mainThread }}%</span>
                    </div>
                    <el-progress :percentage="overview.serverStatus.mainThread" :color="getPressureColor(overview.serverStatus.mainThread)" :show-text="false" :stroke-width="8"></el-progress>
                    <div class="metric-desc">基准目标 20ms Tick 耗时比率与心跳滞后计算</div>
                  </div>

                  <!-- 2. CPU 综合压力 (权重 25%) -->
                  <div class="metric-item">
                    <div class="metric-header">
                      <span class="metric-name">CPU 综合压力</span>
                      <span class="metric-weight">权重 25%</span>
                      <span class="metric-val" :style="{ color: getPressureColor(overview.serverStatus.cpu) }">{{ overview.serverStatus.cpu }}%</span>
                    </div>
                    <el-progress :percentage="overview.serverStatus.cpu" :color="getPressureColor(overview.serverStatus.cpu)" :show-text="false" :stroke-width="8"></el-progress>
                    <div class="metric-desc">系统负载、SCPSL 进程及多核线程使用率</div>
                  </div>

                  <!-- 3. 游戏业务压力 (权重 15%) -->
                  <div class="metric-item">
                    <div class="metric-header">
                      <span class="metric-name">游戏业务压力</span>
                      <span class="metric-weight">权重 15%</span>
                      <span class="metric-val" :style="{ color: getPressureColor(overview.serverStatus.game) }">{{ overview.serverStatus.game }}%</span>
                    </div>
                    <el-progress :percentage="overview.serverStatus.game" :color="getPressureColor(overview.serverStatus.game)" :show-text="false" :stroke-width="8"></el-progress>
                    <div class="metric-desc">全服实时在线玩家占比：{{ overview.serverStatus.players }} / {{ overview.serverStatus.maxPlayers }} 人</div>
                  </div>

                  <!-- 4. 内存运行压力 (权重 15%) -->
                  <div class="metric-item">
                    <div class="metric-header">
                      <span class="metric-name">内存运行压力</span>
                      <span class="metric-weight">权重 15%</span>
                      <span class="metric-val" :style="{ color: getPressureColor(overview.serverStatus.memory) }">{{ overview.serverStatus.memory }}%</span>
                    </div>
                    <el-progress :percentage="overview.serverStatus.memory" :color="getPressureColor(overview.serverStatus.memory)" :show-text="false" :stroke-width="8"></el-progress>
                    <div class="metric-desc">进程工作集与可用物理内存综合压力</div>
                  </div>

                  <!-- 5. 网络传输压力 (权重 10%) -->
                  <div class="metric-item">
                    <div class="metric-header">
                      <span class="metric-name">网络传输压力</span>
                      <span class="metric-weight">权重 10%</span>
                      <span class="metric-val" :style="{ color: getPressureColor(overview.serverStatus.network) }">{{ overview.serverStatus.network }}%</span>
                    </div>
                    <el-progress :percentage="overview.serverStatus.network" :color="getPressureColor(overview.serverStatus.network)" :show-text="false" :stroke-width="8"></el-progress>
                    <div class="metric-desc">活跃连接与双向通信心跳延迟评估</div>
                  </div>

                  <!-- 6. 磁盘 I/O 压力 (权重 10%) -->
                  <div class="metric-item">
                    <div class="metric-header">
                      <span class="metric-name">磁盘 I/O 压力</span>
                      <span class="metric-weight">权重 10%</span>
                      <span class="metric-val" :style="{ color: getPressureColor(overview.serverStatus.disk) }">{{ overview.serverStatus.disk }}%</span>
                    </div>
                    <el-progress :percentage="overview.serverStatus.disk" :color="getPressureColor(overview.serverStatus.disk)" :show-text="false" :stroke-width="8"></el-progress>
                    <div class="metric-desc">驱动器分区空间占用与读写吞吐开销</div>
                  </div>
                </div>
              </div>
            </div>
          </el-card>

          <!-- 折线图：玩家数量趋势 -->
          <el-card class="chart-card" shadow="hover">
            <template #header>
              <div class="chart-title">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="color:var(--qb-accent)">
                  <polyline points="22 12 18 12 15 21 9 3 6 12 2 12"></polyline>
                </svg>
                <span>全服在线玩家趋势</span>
              </div>
              <div style="display:flex;align-items:center;gap:12px">
                <el-radio-group v-model="overview.historyRange" size="small" @change="onHistoryRangeChange">
                  <el-radio-button label="1h">最近 1 小时</el-radio-button>
                  <el-radio-button label="6h">最近 6 小时</el-radio-button>
                  <el-radio-button label="24h">最近 1 天</el-radio-button>
                  <el-radio-button label="30d">最近 30 天</el-radio-button>
                </el-radio-group>
              </div>
            </template>

            <div class="chart-container" @mousemove="onChartMouseMove" @mouseleave="onChartMouseLeave">
              <div v-if="filteredHistory.length === 0" class="chart-empty">
                暂无历史采样数据（系统每 30 秒自动记录一次）
              </div>
              <svg v-else class="line-chart-svg" viewBox="0 0 1000 260" preserveAspectRatio="none">
                <defs>
                  <linearGradient id="chartGradient" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stop-color="var(--qb-accent)" stop-opacity="0.32" />
                    <stop offset="100%" stop-color="var(--qb-accent)" stop-opacity="0.0" />
                  </linearGradient>
                </defs>

                <!-- 水平网格线与 Y 轴刻度 -->
                <g class="chart-grid">
                  <g v-for="(grid, idx) in chartYGrid" :key="idx">
                    <line :x1="chartPadL" :y1="grid.y" :x2="1000 - chartPadR" :y2="grid.y"
                          stroke="var(--qb-border)" stroke-dasharray="3,3" stroke-width="1" />
                    <text :x="chartPadL - 10" :y="grid.y + 4" text-anchor="end"
                          fill="var(--qb-text-3)" font-size="11" font-family="var(--qb-mono)">{{ grid.val }}</text>
                  </g>
                </g>

                <!-- 曲线区域填充 -->
                <path v-if="chartAreaPath" :d="chartAreaPath" fill="url(#chartGradient)" />

                <!-- 曲线折线 -->
                <path v-if="chartLinePath" :d="chartLinePath" fill="none"
                      stroke="var(--qb-accent)" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" />

                <!-- X 轴时间标签 -->
                <g class="chart-x-labels">
                  <text v-for="(lbl, idx) in chartXLabels" :key="idx"
                        :x="lbl.x" :y="245" text-anchor="middle"
                        fill="var(--qb-text-3)" font-size="11" font-family="var(--qb-mono)">{{ lbl.text }}</text>
                </g>

                <!-- 鼠标悬浮指示：准星竖线与焦点光圈 -->
                <g v-if="hoverPoint">
                  <line :x1="hoverPoint.x" :y1="chartPadT" :x2="hoverPoint.x" :y2="chartPadT + chartPlotH"
                        stroke="var(--qb-accent)" stroke-width="1.2" stroke-dasharray="3,3" opacity="0.75" />
                  <circle :cx="hoverPoint.x" :cy="hoverPoint.y" r="6"
                          fill="var(--qb-accent)" stroke="var(--qb-surface)" stroke-width="2.5" />
                </g>
              </svg>

              <!-- 悬浮数据浮层 Tooltip -->
              <div v-if="hoverPoint" class="chart-tooltip" :style="hoverTooltipStyle">
                <div class="tooltip-time">{{ formatChartTime(hoverPoint.raw.timestamp) }} ({{ fmtTime(hoverPoint.raw.timestamp) }})</div>
                <div class="tooltip-val">全服在线：{{ hoverPoint.raw.totalOnline }} 人</div>
                <div v-if="hoverPoint.raw.perServer && Object.keys(hoverPoint.raw.perServer).length > 0" class="tooltip-servers">
                  <div v-for="(cnt, sname) in hoverPoint.raw.perServer" :key="sname">
                    {{ sname }}: {{ cnt }} 人
                  </div>
                </div>
              </div>
            </div>
            <div class="muted" style="font-size:12px;margin-top:8px">{{ chartDataSummary }}</div>
          </el-card>

          <!-- 图形化服务器状态表格 -->
          <el-card class="chart-card" shadow="hover">
            <template #header>
              <div class="chart-title">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="color:var(--qb-accent)">
                  <rect x="2" y="2" width="20" height="8" rx="2" ry="2"></rect>
                  <rect x="2" y="14" width="20" height="8" rx="2" ry="2"></rect>
                  <line x1="6" y1="6" x2="6.01" y2="6"></line>
                  <line x1="6" y1="18" x2="6.01" y2="18"></line>
                </svg>
                <span>服务器实时状态</span>
              </div>
            </template>

            <el-empty v-if="overview.servers.length === 0" description="暂无服务器在线（等待游戏插件注册连接）"></el-empty>

            <el-table v-else :data="overview.servers" border stripe style="width:100%">
              <el-table-column label="状态" width="100" align="center">
                <template #default="{ row }">
                  <span class="pulse-dot"></span>
                  <el-tag size="small" type="success" effect="dark">在线</el-tag>
                </template>
              </el-table-column>
              <el-table-column label="服务器名称" min-width="160">
                <template #default="{ row }">
                  <b style="font-size:14px">#{{ row.index }} {{ row.name }}</b>
                  <el-tag v-if="row.isStatic" size="small" type="info" style="margin-left:6px">静态配置</el-tag>
                </template>
              </el-table-column>
              <el-table-column label="玩家负载" min-width="220">
                <template #default="{ row }">
                  <div style="display:flex;align-items:center;gap:10px">
                    <el-progress :percentage="calcProgress(row)"
                                 :color="progressColor(row)"
                                 :stroke-width="12"
                                 style="flex:1"
                                 :format="() => row.onlinePlayers + ' / ' + row.maxPlayers + ' 人'">
                    </el-progress>
                  </div>
                </template>
              </el-table-column>
              <el-table-column label="连接地址" width="210">
                <template #default="{ row }">
                  <code style="font-size:12px">{{ row.connectHost }}:{{ row.port }}</code>
                  <span v-if="row.gamePort" class="muted" style="margin-left:6px">游戏: {{ row.gamePort }}</span>
                </template>
              </el-table-column>
              <el-table-column label="最近心跳" width="170">
                <template #default="{ row }">
                  {{ fmtTime(row.lastHeartbeat) }}
                </template>
              </el-table-column>
              <el-table-column label="快捷操作" width="240" fixed="right">
                <template #default="{ row }">
                  <el-button size="small" @click="openPlayers(row)">玩家列表</el-button>
                  <el-button size="small" type="primary" v-if="can('broadcast.send')" @click="openBroadcast(row)">广播</el-button>
                  <el-button size="small" @click="openInfo(row)">详情</el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </div>
`,
  setup() {
    const overview = useOverview();
    const { openPlayers, openBroadcast, openInfo } = useServers();
    return {
      ...overview,
      openPlayers,
      openBroadcast,
      openInfo,
      can,
      fmtTime,
    };
  }
};
