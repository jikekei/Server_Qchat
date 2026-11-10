import { computed } from '../deps.js';

const metricDefinitions = [
  { key: 'mainThread', label: '游戏主线程' },
  { key: 'cpu', label: 'CPU 综合压力' },
  { key: 'game', label: '游戏业务' },
  { key: 'memory', label: '内存压力' },
  { key: 'network', label: '网络压力' },
  { key: 'disk', label: '磁盘空间压力' },
];

function percentage(value) {
  if (value === null || value === undefined || value === '') return null;
  const number = Number(value);
  return Number.isFinite(number) ? Math.round(Math.max(0, Math.min(100, number))) : null;
}

function pressure(value) {
  if (value === null) return { tone: 'muted', level: '暂无数据' };
  if (value >= 90) return { tone: 'danger', level: '极高压力' };
  if (value >= 70) return { tone: 'warning', level: '高压力' };
  if (value >= 50) return { tone: 'attention', level: '中等压力' };
  return { tone: 'normal', level: '低压力' };
}

function bytes(value) {
  if (!Number.isFinite(value) || value < 0) return '—';
  const gib = value / (1024 ** 3);
  return gib >= 1 ? `${gib.toFixed(1)} GiB` : `${Math.round(value / (1024 ** 2))} MiB`;
}

export const ResourceStatusCard = {
  name: 'ResourceStatusCard',
  props: { status: { type: Object, required: true }, loading: Boolean },
  setup(props) {
    const sampledAt = computed(() => {
      const date = new Date(props.status.updatedAt);
      return props.status.updatedAt && Number.isFinite(date.getTime()) && date.getFullYear() > 2000 ? date : null;
    });
    const ready = computed(() => !!sampledAt.value);
    const sampleTime = computed(() => sampledAt.value?.toLocaleTimeString('zh-CN', { hour12: false }) || '—');
    const load = computed(() => ready.value ? percentage(props.status.load) : null);
    const tone = computed(() => {
      if (!ready.value) return 'muted';
      return ({ Idle: 'normal', Normal: 'normal', Medium: 'attention', High: 'warning', Critical: 'danger', Overload: 'danger' })[props.status.status] || 'muted';
    });
    const metrics = computed(() => metricDefinitions.map(metric => {
      const value = ready.value ? percentage(props.status[metric.key]) : null;
      return { ...metric, value, ...pressure(value) };
    }));
    const detailItems = computed(() => {
      const d = props.status.details;
      if (!ready.value || !d) return [];
      const percent = value => Number.isFinite(value) && value >= 0 ? `${value.toFixed(1)}%` : '—';
      const count = value => Number.isFinite(value) && value >= 0 ? String(value) : '—';
      return [
        { label: '进程组合 CPU', value: percent(d.processCpuPercent) },
        { label: '系统 CPU（估算）', value: percent(d.systemCpuPercent) },
        { label: '进程组合工作集', value: bytes(d.workingSetBytes) },
        { label: '内存容量参考值', value: bytes(d.totalMemoryBytes) },
        { label: '可用内存参考值', value: bytes(d.availableMemoryBytes) },
        { label: '磁盘空间已使用', value: percent(d.diskUsagePercent) },
        { label: '主机 TCP 连接数', value: count(d.activeConnections) },
        { label: '本机游戏进程数', value: count(d.processCount) },
      ];
    });
    return { ready, sampleTime, load, tone, metrics, detailItems, percentage };
  },
  template: `
    <el-card class="chart-card resource-card" shadow="never">
      <template #header>
        <div class="resource-heading">
          <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
            <rect x="5" y="5" width="14" height="14" rx="3"/><rect x="9" y="9" width="6" height="6" rx="1"/>
            <path d="M9 2v3m6-3v3M9 19v3m6-3v3M2 9h3m-3 6h3m14-6h3m-3 6h3"/>
          </svg>
          <h2>服务器资源报表</h2>
        </div>
        <span class="resource-sample">{{ loading ? '正在同步…' : (ready ? '采集于 ' + sampleTime : '等待首次采集') }}</span>
      </template>

      <div class="resource-summary" :data-tone="tone" aria-label="综合负载摘要">
        <div class="resource-total"><span>综合负载</span><strong>{{ load ?? '—' }}<small v-if="load !== null">%</small></strong><span class="resource-badge">{{ ready ? (status.statusText || '状态未知') : '待采集' }}</span></div>
        <dl class="resource-summary-stats">
          <div><dt>平滑</dt><dd>{{ ready ? (percentage(status.smoothLoad) ?? '—') : '—' }}<small v-if="ready && percentage(status.smoothLoad) !== null">%</small></dd></div>
          <div><dt>60 秒峰值</dt><dd>{{ ready ? (percentage(status.peakLoad) ?? '—') : '—' }}<small v-if="ready && percentage(status.peakLoad) !== null">%</small></dd></div>
        </dl>
      </div>

      <table class="resource-report">
        <caption class="resource-sr-only">六项资源压力指数</caption>
        <colgroup><col class="resource-col-name"><col class="resource-col-value"><col class="resource-col-state"></colgroup>
        <thead><tr><th scope="col">指标</th><th scope="col">压力指数</th><th scope="col">状态</th></tr></thead>
        <tbody>
          <tr v-for="metric in metrics" :key="metric.key" :data-tone="metric.tone">
            <th scope="row">{{ metric.label }}</th>
            <td><div class="resource-reading"><strong>{{ metric.value ?? '—' }}<small v-if="metric.value !== null">%</small></strong><div class="resource-track" aria-hidden="true"><span :style="{ transform: 'scaleX(' + ((metric.value ?? 0) / 100) + ')' }"></span></div></div></td>
            <td><span class="resource-pressure-label">{{ metric.level }}</span></td>
          </tr>
        </tbody>
      </table>

      <dl class="resource-bottlenecks">
        <div><dt>主要压力</dt><dd>{{ ready ? (status.primaryBottleneck || '暂无') : '—' }}</dd></div>
        <div><dt>次要压力</dt><dd>{{ ready ? (status.secondaryBottleneck || '暂无') : '—' }}</dd></div>
      </dl>
      <section class="resource-diagnosis"><h3>运行建议</h3><p>{{ ready ? (status.diagnosis || '暂无运行建议') : '等待首次采集，完成后显示负载与运行建议。' }}</p></section>

      <details class="resource-details">
        <summary>采集明细与指标说明<span aria-hidden="true">展开 / 收起</span></summary>
        <dl v-if="detailItems.length" class="resource-details-grid"><div v-for="item in detailItems" :key="item.label"><dt>{{ item.label }}</dt><dd>{{ item.value }}</dd></div></dl>
        <p>压力指数用于识别运行瓶颈，不等同于硬件实际使用率。主机数据来自面板所在机器，进程组合包含面板与本机 SCPSL 进程；内存容量与可用值来自运行时，可作为参考。综合负载包含峰值保护。</p>
      </details>
    </el-card>
  `,
};
