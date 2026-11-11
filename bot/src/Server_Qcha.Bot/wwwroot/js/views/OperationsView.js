import { ref, onMounted, ElMessage } from '../deps.js';
import { api, can } from '../api.js';

export const OperationsView = {
  name: 'OperationsView',
  template: `
  <div v-if="can('accounts.manage')">
    <div class="page-header">
      <div class="page-header-meta">
        <h3 class="page-title">运维 API 与历史日志归档</h3>
        <span class="page-subtitle">配置独立 Bearer 自动化运维接口令牌，以及按日期检索全量服务器历史归档日志（含 stdout）</span>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon style="margin-bottom:14px" />

    <div class="ops-grid">
      <!-- 左侧：Bearer 令牌与权限控制 -->
      <el-card shadow="never">
        <template #header>
          <div class="card-head">
            <div class="card-head-title-group">
              <span>自动化运维 API 鉴权配置</span>
              <span class="card-head-subtitle">通过独立 Bearer 令牌供外部监控或脚本调用</span>
            </div>
            <el-tag size="small" :type="enabled ? 'success' : 'info'">{{ enabled ? 'API 已启用' : 'API 已关闭' }}</el-tag>
          </div>
        </template>

        <div style="display:flex;flex-direction:column;gap:16px">
          <div class="metric-tiles" style="grid-template-columns: 1fr 1fr">
            <div class="metric-tile">
              <span class="metric-tile-label">运维 API 总开关</span>
              <div style="margin-top:4px">
                <el-switch v-model="enabled" active-text="启用接口" inactive-text="禁用" />
              </div>
            </div>
            <div class="metric-tile">
              <span class="metric-tile-label">进程生命周期控制权限</span>
              <div style="margin-top:4px">
                <el-switch v-model="control" active-text="允许启停/重启" inactive-text="仅只读" />
              </div>
            </div>
          </div>

          <div style="font-size:12.5px;color:var(--c-text-secondary);line-height:1.6">
            请求头格式：<code>Authorization: Bearer &lt;令牌&gt;</code><br>
            安全策略：令牌仅在首次生成或重置后明文回显一次，重置后旧令牌立即失效。
          </div>

          <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center">
            <el-button type="primary" @click="save(false)">保存开关配置</el-button>
            <el-button type="warning" plain @click="save(true)">生成 / 重置 Bearer 令牌</el-button>
            <span v-if="message" style="font-size:12.5px;color:var(--c-success);font-weight:600">{{ message }}</span>
          </div>

          <div v-if="key" class="token-reveal-box">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:6px">
              <span style="font-size:12px;font-weight:700;color:var(--c-success)">新生成的 Bearer 令牌（请立即复制保存）：</span>
              <el-button size="small" type="success" plain @click="copyToken">复制令牌</el-button>
            </div>
            <pre>{{ key }}</pre>
          </div>
        </div>
      </el-card>

      <!-- 右侧：接口速查清单 -->
      <el-card shadow="never">
        <template #header>
          <div class="card-head">
            <div class="card-head-title-group">
              <span>运维 API 端点参考 (/api/ops/v1)</span>
              <span class="card-head-subtitle">所有接口均需携带 Authorization Bearer 请求头</span>
            </div>
          </div>
        </template>

        <div class="endpoint-list">
          <div class="endpoint-item" v-for="ep in endpoints" :key="ep.path">
            <span class="endpoint-method" :class="ep.method.toLowerCase()">{{ ep.method }}</span>
            <span class="endpoint-path">{{ ep.path }}</span>
            <span class="endpoint-desc">{{ ep.desc }}</span>
          </div>
        </div>
      </el-card>
    </div>

    <!-- 底部：服务器日志归档查阅器 -->
    <el-card shadow="never">
      <template #header>
        <div class="card-head">
          <div class="card-head-title-group">
            <span>服务器历史日志归档（含完整 stdout）</span>
            <span class="card-head-subtitle">共 {{ servers.length }} 个托管服务器实例 · 日期按服务端本地时间计算</span>
          </div>
          <div style="display:flex;gap:8px;align-items:center">
            <el-button size="small" @click="listFiles">刷新文件列表</el-button>
            <el-button v-if="more" size="small" type="primary" @click="readFile(false)">读取下一页日志</el-button>
          </div>
        </div>
      </template>

      <div class="toolbar" style="margin-bottom:14px">
        <el-select v-model="serverId" placeholder="选择托管服务器" style="width:260px" @change="loadDates">
          <el-option v-for="s in servers" :key="s.id" :value="s.id" :label="(s.name || s.id) + ' (' + s.id + ')'" />
        </el-select>

        <el-select v-model="selectedDate" placeholder="选择归档日期" style="width:220px" @change="listFiles">
          <el-option value="today" :label="'今天 (' + (today || 'today') + ')'" />
          <el-option value="" label="全部历史日期" />
          <el-option v-for="d in dates" :key="d.date" :value="d.date" :label="d.date + '（' + d.count + ' 个文件）'" />
        </el-select>

        <el-select v-model="file" placeholder="选择要查阅的日志文件..." style="flex:1;min-width:260px" @change="readFile(true)">
          <el-option v-for="f in files" :key="f.name" :value="f.name" :label="f.name" />
        </el-select>
      </div>

      <div class="terminal-window">
        <div class="terminal-header">
          <div class="terminal-title">
            <span class="terminal-dots"><i></i><i></i><i></i></span>
            <span>ARCHIVE VIEWER</span>
            <span class="muted" style="font-size:11px">· {{ file || (serverId && !files.length ? '所选日期暂无归档日志文件' : '请选择日志文件') }}</span>
          </div>
          <div class="terminal-controls" v-if="more">
            <el-button size="small" type="primary" @click="readFile(false)">继续加载下一页</el-button>
          </div>
        </div>

        <pre class="terminal" style="margin:0;height:460px;white-space:pre-wrap;overflow-wrap:anywhere">{{ content || (serverId && !files.length ? '（所选日期暂无日志文件）' : '（在上方选择服务器、日期与归档文件后在此处显示内容）') }}</pre>
      </div>
    </el-card>
  </div>`,
  setup() {
    const enabled = ref(false), control = ref(false), key = ref(''), error = ref(''), message = ref('');
    const endpoints = [
      { method: 'GET', path: '/api/ops/v1/panel/logs', desc: '最近 2000 条面板运行日志' },
      { method: 'GET', path: '/api/ops/v1/servers', desc: '服务器状态列表' },
      { method: 'GET', path: '/api/ops/v1/servers/{id}', desc: '单服详细状态' },
      { method: 'GET', path: '/api/ops/v1/servers/{id}/logs?after=0&limit=500', desc: '增量服务器日志' },
      { method: 'GET', path: '/api/ops/v1/log-servers', desc: '服务器数量和列表' },
      { method: 'GET', path: '/api/ops/v1/servers/{id}/log-dates', desc: '有日志的所有日期' },
      { method: 'GET', path: '/api/ops/v1/servers/{id}/log-files?date=today', desc: '指定日期日志文件' },
      { method: 'GET', path: '/api/ops/v1/servers/{id}/archive?file=文件名&offset=0', desc: '分页读取归档文件' },
      { method: 'POST', path: '/api/ops/v1/servers/{id}/start', desc: '启动指定服务器' },
      { method: 'POST', path: '/api/ops/v1/servers/{id}/stop', desc: '停止指定服务器' },
      { method: 'POST', path: '/api/ops/v1/servers/{id}/restart', desc: '重启指定服务器' },
    ];

    onMounted(async () => {
      if (!can('accounts.manage')) return;
      try {
        const s = await api('/operations/settings');
        enabled.value = s.enabled;
        control.value = s.allowControl;
      } catch (e) {
        error.value = e.message;
      }
    });

    async function save(rotateKey) {
      error.value = '';
      try {
        const s = await api('/operations/settings', {
          method: 'PUT',
          body: JSON.stringify({ enabled: enabled.value, allowControl: control.value, rotateKey })
        });
        key.value = s.key || '';
        message.value = '配置已保存，立即生效';
        ElMessage.success('运维 API 配置已保存');
      } catch (e) {
        error.value = e.message;
      }
    }

    async function copyToken() {
      if (!key.value) return;
      try {
        await navigator.clipboard.writeText(key.value);
        ElMessage.success('令牌已复制到剪贴板');
      } catch {
        ElMessage.info('请手动选中上方令牌文本复制');
      }
    }

    const serverId = ref(''), servers = ref([]), dates = ref([]), today = ref(''), selectedDate = ref('today'), files = ref([]), file = ref(''), content = ref(''), more = ref(false);
    let offset = 0, revision = 0;

    onMounted(async () => {
      if (!can('server.control')) return;
      try {
        const r = await api('/local/servers');
        servers.value = r.servers || [];
        if (servers.value.length) {
          serverId.value = servers.value[0].id;
          await loadDates();
        }
      } catch (e) {
        error.value = e.message;
      }
    });

    async function loadDates() {
      const id = serverId.value;
      revision++;
      dates.value = [];
      files.value = [];
      file.value = '';
      content.value = '';
      more.value = false;
      try {
        const r = await api('/operations/log-dates/' + encodeURIComponent(id));
        if (id !== serverId.value) return;
        dates.value = r.dates;
        today.value = r.today;
        await listFiles();
      } catch (e) {
        error.value = e.message;
      }
    }

    async function listFiles() {
      const current = ++revision;
      file.value = '';
      content.value = '';
      more.value = false;
      files.value = [];
      if (!serverId.value) return;
      try {
        error.value = '';
        const r = await api('/operations/archive/' + encodeURIComponent(serverId.value) + '?date=' + encodeURIComponent(selectedDate.value));
        if (current !== revision) return;
        files.value = r.files;
      } catch (e) {
        if (current === revision) error.value = e.message;
      }
    }

    async function readFile(reset) {
      const current = ++revision;
      if (!file.value) return;
      try {
        error.value = '';
        if (reset) {
          offset = 0;
          content.value = '';
        }
        const r = await api('/operations/archive/' + encodeURIComponent(serverId.value) + '?file=' + encodeURIComponent(file.value) + '&date=' + encodeURIComponent(selectedDate.value) + '&offset=' + offset);
        if (current !== revision) return;
        content.value = r.lines.join('');
        offset = r.next;
        more.value = r.more;
      } catch (e) {
        if (current === revision) error.value = e.message;
      }
    }

    return {
      can, enabled, control, key, error, message, endpoints, save, copyToken,
      serverId, servers, dates, today, selectedDate, loadDates, files, file, content, more, listFiles, readFile
    };
  }
};
