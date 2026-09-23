import { reactive, computed } from '../deps.js';
import { api, token, page } from '../api.js';
import { ElMessage } from '../deps.js';

const overview = reactive({
  loading: false,
  lastUpdated: null,
  stats: {
    serverCount: 0,
    onlineServers: 0,
    totalServers: 0,
    localServerCount: 0,
    localRunningCount: 0,
    localAdminEnabled: false,
    totalOnline: 0,
    totalMax: 0,
    peakToday: 0,
    botConnected: false,
    botPlatform: '',
    botUserId: '',
    botNickname: '',
    botLatencyMs: -1,
    mysqlConnected: false,
    dbConfigured: false,
    dbSummary: null
  },
  servers: [],
  history: [],
  historyRange: '24h',
  hoverIdx: -1
});

async function loadOverview() {
  overview.loading = true;
  try {
    const data = await api(`/overview?historyRange=${encodeURIComponent(overview.historyRange)}`);
    if (data) {
      const s = data.stats || {};
      const servers = data.servers || [];

      // 服务器数量：优先 localServerCount / serverCount / totalServers / servers.length
      const sc = s.serverCount ?? s.localServerCount ?? s.totalServers ?? servers.length;
      overview.stats.serverCount = sc;
      overview.stats.onlineServers = s.onlineServers ?? servers.filter(x => x.isOnline).length;
      overview.stats.totalServers = s.totalServers ?? servers.length;
      overview.stats.localServerCount = s.localServerCount ?? 0;
      overview.stats.localRunningCount = s.localRunningCount ?? 0;
      overview.stats.localAdminEnabled = !!s.localAdminEnabled;

      // 实时玩家与承载上限
      overview.stats.totalOnline = s.totalOnline ?? s.totalOnlinePlayers ?? servers.filter(x => x.isOnline).reduce((sum, x) => sum + (x.onlinePlayers || 0), 0);
      overview.stats.totalMax = s.totalMax ?? s.totalMaxPlayers ?? servers.reduce((sum, x) => sum + (x.maxPlayers || 0), 0);
      overview.stats.peakToday = s.peakToday ?? overview.stats.totalOnline;

      // QQ 机器人状态
      overview.stats.botConnected = !!s.botConnected;
      overview.stats.botPlatform = s.botPlatform || 'Unknown';
      overview.stats.botUserId = s.botUserId || '';
      overview.stats.botNickname = s.botNickname || '';
      overview.stats.botLatencyMs = s.botLatencyMs ?? -1;

      // MySQL 状态：兼容直接布尔值与 dbSummary.isConnected
      overview.stats.mysqlConnected = typeof s.mysqlConnected === 'boolean'
        ? s.mysqlConnected
        : !!(s.dbSummary && (s.dbSummary.isConnected || s.dbSummary.connected || (!s.dbSummary.error && s.dbConfigured)));
      overview.stats.dbConfigured = !!s.dbConfigured;
      overview.stats.dbSummary = s.dbSummary || null;

      overview.servers = servers;
      overview.history = data.history || [];
      overview.lastUpdated = new Date();
    }
  } catch (e) {
    ElMessage.error('获取总览数据失败：' + e.message);
  } finally {
    overview.loading = false;
  }
}

async function onHistoryRangeChange() {
  overview.hoverIdx = -1;
  await loadOverview();
}

// 周期性轮询总览数据（15秒一次）
let overviewTimer = null;
function startOverviewPolling() {
  if (overviewTimer) clearInterval(overviewTimer);
  overviewTimer = setInterval(() => {
    if (token.value && page.value === 'overview') {
      loadOverview();
    }
  }, 15000);
}

// 折线图坐标常量
const chartPadL = 50;
const chartPadR = 25;
const chartPadT = 20;
const chartPadB = 35;
const chartPlotW = 1000 - chartPadL - chartPadR; // 925
const chartPlotH = 260 - chartPadT - chartPadB;  // 205

const chartRangeMs = computed(() => ({
  '1h': 60 * 60 * 1000,
  '6h': 6 * 60 * 60 * 1000,
  '24h': 24 * 60 * 60 * 1000,
  '30d': 30 * 24 * 60 * 60 * 1000,
}[overview.historyRange] || 24 * 60 * 60 * 1000));

// 历史不足所选窗口时，以当前可用数据的最早时间作为横轴起点，让已有曲线铺满图表。
const chartWindow = computed(() => {
  const end = overview.lastUpdated ? new Date(overview.lastUpdated).getTime() : Date.now();
  const requestedStart = end - chartRangeMs.value;
  let firstAvailable = Infinity;
  for (const point of overview.history || []) {
    const timestamp = new Date(point.timestamp).getTime();
    if (Number.isFinite(timestamp) && timestamp >= requestedStart && timestamp <= end && timestamp < firstAvailable) {
      firstAvailable = timestamp;
    }
  }
  const start = Number.isFinite(firstAvailable) && firstAvailable > requestedStart
    ? firstAvailable
    : requestedStart;
  return { start, end, duration: Math.max(1, end - start) };
});

// 根据真实时间范围筛选采样点。历史尚未积累到所选范围时，图表只显示已有部分。
const filteredHistory = computed(() => {
  const all = overview.history || [];
  const start = chartWindow.value.start;
  const end = chartWindow.value.end;
  return all.filter(point => {
    const timestamp = new Date(point.timestamp).getTime();
    return Number.isFinite(timestamp) && timestamp >= start && timestamp <= end;
  });
});

const chartDataSummary = computed(() => {
  const points = filteredHistory.value;
  if (points.length === 0) return '所选时间范围内暂无采样数据';
  const first = formatChartTime(points[0].timestamp);
  const last = formatChartTime(points[points.length - 1].timestamp);
  return `当前区间实际采样 ${points.length} 个点：${first} 至 ${last}`;
});

// 计算 Y 轴最大值和刻度
const chartMaxY = computed(() => {
  const list = filteredHistory.value;
  let m = 0;
  for (const p of list) {
    if (p.totalOnline > m) m = p.totalOnline;
  }
  if (m <= 5) return 5;
  if (m <= 10) return 10;
  if (m <= 20) return 20;
  if (m <= 50) return 50;
  return Math.ceil((m * 1.25) / 10) * 10;
});

const chartYGrid = computed(() => {
  const maxY = chartMaxY.value;
  const steps = maxY <= 5 ? maxY : 5;
  const grids = [];
  for (let i = 0; i <= steps; i++) {
    const val = Math.round((maxY / steps) * i);
    const y = chartPadT + chartPlotH - (val / maxY) * chartPlotH;
    grids.push({ val, y });
  }
  return grids;
});

// 计算各点在 SVG 中的坐标 (x, y)
const chartPoints = computed(() => {
  const list = filteredHistory.value;
  if (!list || list.length === 0) return [];
  const maxY = chartMaxY.value;
  const n = list.length;
  if (n === 1) {
    const y = chartPadT + chartPlotH - (list[0].totalOnline / maxY) * chartPlotH;
    const timestamp = new Date(list[0].timestamp).getTime();
    const x = chartPadL + ((timestamp - chartWindow.value.start) / chartWindow.value.duration) * chartPlotW;
    return [{ x, y, raw: list[0] }];
  }
  return list.map((item, idx) => {
    const timestamp = new Date(item.timestamp).getTime();
    const x = chartPadL + ((timestamp - chartWindow.value.start) / chartWindow.value.duration) * chartPlotW;
    const y = chartPadT + chartPlotH - (item.totalOnline / maxY) * chartPlotH;
    return { x, y, raw: item };
  });
});

// 生成三次贝塞尔平滑曲线路径
const chartLinePath = computed(() => {
  const pts = chartPoints.value;
  if (!pts || pts.length === 0) return '';
  if (pts.length === 1) {
    return `M ${pts[0].x.toFixed(1)} ${pts[0].y.toFixed(1)} L ${(pts[0].x + 0.1).toFixed(1)} ${pts[0].y.toFixed(1)}`;
  }
  let d = `M ${pts[0].x.toFixed(1)} ${pts[0].y.toFixed(1)}`;
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i === 0 ? 0 : i - 1];
    const p1 = pts[i];
    const p2 = pts[i + 1];
    const p3 = pts[i + 2] || p2;
    const cp1x = p1.x + (p2.x - p0.x) / 6;
    const cp1y = p1.y + (p2.y - p0.y) / 6;
    const cp2x = p2.x - (p3.x - p1.x) / 6;
    const cp2y = p2.y - (p3.y - p1.y) / 6;
    d += ` C ${cp1x.toFixed(1)} ${cp1y.toFixed(1)}, ${cp2x.toFixed(1)} ${cp2y.toFixed(1)}, ${p2.x.toFixed(1)} ${p2.y.toFixed(1)}`;
  }
  return d;
});

// 区域填充路径
const chartAreaPath = computed(() => {
  const pts = chartPoints.value;
  if (!pts || pts.length === 0) return '';
  const line = chartLinePath.value;
  if (!line) return '';
  const bottomY = (chartPadT + chartPlotH).toFixed(1);
  const lastX = pts[pts.length - 1].x.toFixed(1);
  const firstX = pts[0].x.toFixed(1);
  return `${line} L ${lastX} ${bottomY} L ${firstX} ${bottomY} Z`;
});

// X 轴时间刻度标签
const chartXLabels = computed(() => {
  const { start, end, duration } = chartWindow.value;
  const count = 6;
  return Array.from({ length: count }, (_, index) => {
    const timestamp = start + (duration * index) / (count - 1);
    const x = chartPadL + (index / (count - 1)) * chartPlotW;
    return { x, text: formatChartTime(timestamp) };
  });
});

function formatChartTime(timestamp) {
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) return '';
  const pad = value => String(value).padStart(2, '0');
  const time = `${pad(date.getHours())}:${pad(date.getMinutes())}`;
  if (overview.historyRange === '1h') {
    return `${time}:${pad(date.getSeconds())}`;
  }
  if (overview.historyRange === '30d') {
    return `${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${time}`;
  }
  return time;
}

// 悬浮点与 Tooltip 计算
const hoverPoint = computed(() => {
  const pts = chartPoints.value;
  if (!pts || pts.length === 0 || overview.hoverIdx < 0 || overview.hoverIdx >= pts.length) {
    return null;
  }
  return pts[overview.hoverIdx];
});

const hoverTooltipStyle = computed(() => {
  const pt = hoverPoint.value;
  if (!pt) return { display: 'none' };
  const leftPct = (pt.x / 1000) * 100;
  const topPct = (pt.y / 260) * 100;
  return {
    left: `${leftPct}%`,
    top: `${topPct}%`,
    opacity: 1
  };
});

function onChartMouseMove(e) {
  const pts = chartPoints.value;
  if (!pts || pts.length === 0) return;
  const rect = e.currentTarget.getBoundingClientRect();
  const clientX = e.clientX - rect.left;
  const svgX = (clientX / rect.width) * 1000;

  let closestIdx = 0;
  let minDiff = Infinity;
  for (let i = 0; i < pts.length; i++) {
    const diff = Math.abs(pts[i].x - svgX);
    if (diff < minDiff) {
      minDiff = diff;
      closestIdx = i;
    }
  }
  overview.hoverIdx = closestIdx;
}

function onChartMouseLeave() {
  overview.hoverIdx = -1;
}

function calcProgress(row) {
  if (!row || !row.maxPlayers || row.maxPlayers <= 0) return 0;
  return Math.min(100, Math.round(((row.onlinePlayers || 0) / row.maxPlayers) * 100));
}

function progressColor(row) {
  const pct = calcProgress(row);
  if (pct >= 90) return '#f56c6c';
  if (pct >= 70) return '#e6a23c';
  return '#10b981';
}

export function useOverview() {
  return {
    overview,
    loadOverview,
    onHistoryRangeChange,
    startOverviewPolling,
    filteredHistory,
    chartDataSummary,
    chartMaxY,
    chartYGrid,
    chartPoints,
    chartLinePath,
    chartAreaPath,
    chartXLabels,
    formatChartTime,
    hoverPoint,
    hoverTooltipStyle,
    onChartMouseMove,
    onChartMouseLeave,
    calcProgress,
    progressColor,
    chartPadL,
    chartPadR,
    chartPadT,
    chartPlotH,
  };
}
