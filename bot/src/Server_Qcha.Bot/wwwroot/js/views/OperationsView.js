import { ref, onMounted, ElMessage, ElMessageBox } from '../deps.js';
import { api, can } from '../api.js';

export const OperationsView = {
  name: 'OperationsView',
  template: `
  <div v-if="can('accounts.manage')">
    <div class="page-header">
      <div class="page-header-meta">
        <h3 class="page-title">API 与日志归档</h3>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon style="margin-bottom:14px" />

    <el-card shadow="never" style="margin-bottom:16px">
      <template #header>
        <div class="card-head">
          <div class="card-head-title-group">
            <span>运维 API 设置</span>
          </div>
        </div>
      </template>

      <div style="display:flex;flex-direction:column;gap:16px">
        <div class="toolbar">
          <el-switch v-model="enabled" active-text="启用运维 API" />
          <el-switch v-model="control" active-text="允许运维控制与机器人指令" />
        </div>

        <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center">
          <el-button type="primary" :loading="saving" :disabled="!settingsLoaded" @click="save">保存设置</el-button>
        </div>

        <h4 style="margin:0">令牌管理</h4>
        <p style="margin:0;color:var(--c-text-secondary)">为每个调用方创建独立令牌。令牌原文仅在创建时显示。</p>
        <form class="toolbar" @submit.prevent="createToken">
          <el-input v-model="tokenName" aria-label="令牌名称" placeholder="令牌名称，例如监控服务" maxlength="80" style="width:240px" />
          <el-button type="primary" plain native-type="submit" :disabled="!settingsLoaded || !tokenName.trim() || !!tokenAction" :loading="tokenAction === 'create'">创建令牌</el-button>
        </form>

        <div v-if="key" class="token-reveal-box">
          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:6px">
            <span style="font-size:12px;font-weight:700;color:var(--c-success)">新生成的 Bearer 令牌（请立即复制保存）：</span>
            <el-button size="small" type="success" plain @click="copyToken">复制令牌</el-button>
          </div>
          <pre>{{ key }}</pre>
        </div>
        <el-table v-if="settingsLoaded" :data="tokens" row-key="id" empty-text="暂无令牌">
          <el-table-column prop="name" label="令牌名称" />
          <el-table-column label="操作" width="110">
            <template #default="{ row }">
              <el-button size="small" type="danger" plain :disabled="!!tokenAction" :aria-label="'撤销令牌 ' + row.name" @click="revokeToken(row)">撤销</el-button>
            </template>
          </el-table-column>
        </el-table>
        <details class="ops-api-reference">
          <summary>API 接口参考</summary>
          <p>请求头 <code>Authorization: Bearer &lt;令牌&gt;</code></p>
          <div class="endpoint-list">
            <div class="endpoint-item" v-for="ep in endpoints" :key="ep.path">
              <span class="endpoint-method" :class="ep.method.toLowerCase()">{{ ep.method }}</span>
              <span class="endpoint-path">{{ ep.path }}</span>
              <span class="endpoint-desc">{{ ep.desc }}</span>
            </div>
          </div>
        </details>
      </div>
    </el-card>

    <!-- 底部：服务器日志归档查阅器 -->
    <el-card shadow="never">
      <template #header>
        <div class="card-head">
          <div class="card-head-title-group">
            <span>服务器日志归档</span>
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
        <pre class="terminal" style="margin:0;height:460px;white-space:pre-wrap;overflow-wrap:anywhere">{{ content || (serverId && !files.length ? '（所选日期暂无日志文件）' : '请选择日志文件') }}</pre>
      </div>
    </el-card>
  </div>`,
  setup() {
    const enabled = ref(false), control = ref(false), key = ref(''), error = ref('');
    const settingsLoaded = ref(false), saving = ref(false), tokens = ref([]), tokenName = ref(''), tokenAction = ref('');
    let revealedTokenId = '';
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
      { method: 'POST', path: '/api/ops/v1/bot/commands', desc: '执行 QQ 机器人指令并返回回复' },
    ];

    onMounted(async () => {
      if (!can('accounts.manage')) return;
      try {
        const s = await api('/operations/settings');
        enabled.value = s.enabled;
        control.value = s.allowControl;
        tokens.value = s.tokens;
        settingsLoaded.value = true;
      } catch (e) {
        error.value = e.message;
      }
    });

    async function save() {
      if (saving.value || !settingsLoaded.value) return;
      saving.value = true;
      error.value = '';
      try {
        await api('/operations/settings', {
          method: 'PUT',
          body: JSON.stringify({ enabled: enabled.value, allowControl: control.value })
        });
        ElMessage.success('运维 API 配置已保存');
      } catch (e) {
        error.value = e.message;
      } finally {
        saving.value = false;
      }
    }

    async function createToken() {
      const name = tokenName.value.trim();
      if (!name || tokenAction.value || !settingsLoaded.value) return;
      tokenAction.value = 'create';
      error.value = '';
      try {
        const token = await api('/operations/tokens', {
          method: 'POST',
          body: JSON.stringify({ name })
        });
        tokens.value.push({ id: token.id, name: token.name });
        revealedTokenId = token.id;
        key.value = token.key;
        tokenName.value = '';
        ElMessage.success('令牌已创建');
      } catch (e) {
        error.value = e.message;
      } finally {
        tokenAction.value = '';
      }
    }

    async function revokeToken(token) {
      if (tokenAction.value) return;
      tokenAction.value = token.id;
      error.value = '';
      try {
        try {
          await ElMessageBox.confirm('撤销后，使用该令牌的请求将被拒绝。', '撤销令牌「' + token.name + '」？', {
            type: 'warning', confirmButtonText: '撤销令牌', cancelButtonText: '取消', autofocus: false
          });
        } catch {
          return;
        }
        await api('/operations/tokens/' + encodeURIComponent(token.id), { method: 'DELETE' });
        tokens.value = tokens.value.filter(item => item.id !== token.id);
        if (revealedTokenId === token.id) {
          key.value = '';
          revealedTokenId = '';
        }
        ElMessage.success('令牌已撤销');
      } catch (e) {
        error.value = e.message;
      } finally {
        tokenAction.value = '';
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
      can, enabled, control, key, error, endpoints, save, copyToken,
      settingsLoaded, saving, tokens, tokenName, tokenAction, createToken, revokeToken,
      serverId, servers, dates, today, selectedDate, loadDates, files, file, content, more, listFiles, readFile
    };
  }
};
