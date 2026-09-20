import { ref, reactive, computed, nextTick } from '../deps.js';
import { api, page } from '../api.js';
import { ElMessage, ElMessageBox } from '../deps.js';

// ---- LocalAdmin：本地服务器进程 ----
const local = reactive({
  servers: [], selectedId: '', status: null, lines: [], nextSeq: 0, command: '',
  definitions: [], source: '', configPath: '',
  consoleLevels: [], consoleLevel: 'all', resaving: false,
  autoScroll: true, heartbeatSwitch: false, loading: false, sending: false, truncated: false,
});
const termRef = ref(null);
let localTimer = null;
const MAX_LOCAL_LINES = 1500;

const localRunning = computed(() => !!(local.status && local.status.running));

function localBase() {
  return '/local/servers/' + encodeURIComponent(local.selectedId);
}

function syncHeartbeatSwitch() {
  local.heartbeatSwitch = !!(local.status && local.status.heartbeatStatus !== 'disabled');
}

// 控制台显示级别只在这里（选中项变化 / 列表刷新后）同步
function syncConsoleLevel() {
  if (local.status && local.status.consoleLevel) {
    local.consoleLevel = local.status.consoleLevel;
  }
}

async function localSetConsoleLevel(level) {
  try {
    const r = await api(localBase() + '/console-level', {
      method: 'POST', body: JSON.stringify({ level }),
    });
    if (r.success) {
      ElMessage.success(r.response || '已切换');
      if (r.warning) ElMessage.warning(r.warning);
      await pollLocal();
    } else {
      ElMessage.error(r.error || '切换失败');
      syncConsoleLevel();   // 回滚下拉显示
    }
  } catch (e) {
    ElMessage.error(e.message);
    syncConsoleLevel();
  }
}

// ---- 服务器配置弹窗（增 / 改 / 删） ----
function defaultServerForm() {
  return {
    id: '', name: '', executablePath: '', workingDirectory: '', gamePort: 7777,
    extraArguments: '', autoStart: false,
    enableHeartbeat: true, heartbeatSpanMaxThreshold: 30, heartbeatRestartInSeconds: 11,
    restartOnCrash: true, restartLimit: 4, restartTimeWindowSeconds: 480,
    gracefulStopTimeoutSeconds: 30,
    laToSlBufferSize: 25000, slToLaBufferSize: 200000,
    disableAnsiColors: true, redirectStandardStreams: true,
    consoleLevel: 'all',
  };
}

const serverDialog = reactive({
  visible: false, isEdit: false, loading: false, warning: '',
  discovering: false, candidates: [], hint: '',
  form: defaultServerForm(),
});

// 自动探测 SCPSL.exe：优先本机正在运行的进程，其次 Steam 各库
async function discoverExecutable(autoFill) {
  serverDialog.discovering = true;
  try {
    const r = await api('/local/executables');
    serverDialog.candidates = r.candidates || [];

    if (r.recommended && (autoFill || !serverDialog.form.executablePath)) {
      serverDialog.form.executablePath = r.recommended;
    }

    if (serverDialog.candidates.length) {
      serverDialog.hint = '已找到 ' + serverDialog.candidates.length + ' 个可能的路径' +
        (autoFill && r.recommended ? '，已自动填入' : '') + '，可下拉切换';
    } else {
      const n = (r.steamRoots || []).length;
      serverDialog.hint = '未自动找到 SCPSL.exe，请手动填写完整路径' +
        (n ? '（已扫描 ' + n + ' 个 Steam 库目录）' : '');
    }
  } catch (e) {
    serverDialog.hint = '自动查找失败：' + e.message;
  } finally {
    serverDialog.discovering = false;
  }
}

function openServerDialog(id) {
  serverDialog.warning = '';
  serverDialog.hint = '';
  serverDialog.candidates = [];
  const def = id ? (local.definitions || []).find(d => d.id === id) : null;
  serverDialog.isEdit = !!def;
  serverDialog.form = def ? Object.assign(defaultServerForm(), JSON.parse(JSON.stringify(def)))
                          : defaultServerForm();
  serverDialog.visible = true;

  if (!serverDialog.form.executablePath) {
    discoverExecutable(!serverDialog.isEdit);
  }
}

async function saveServer() {
  const f = serverDialog.form;
  if (!f.name && !f.id) { ElMessage.warning('请填写名称'); return; }
  if (!f.executablePath) { ElMessage.warning('请填写可执行文件路径'); return; }
  if (!f.gamePort) { ElMessage.warning('请填写游戏端口'); return; }

  serverDialog.loading = true;
  try {
    const path = serverDialog.isEdit
      ? '/local/servers/' + encodeURIComponent(local.selectedId)
      : '/local/servers';
    const r = await api(path, {
      method: serverDialog.isEdit ? 'PUT' : 'POST',
      body: JSON.stringify(f),
    });
    ElMessage.success(r.response || '已保存');
    if (r.warning) ElMessage.warning(r.warning);
    serverDialog.visible = false;
    await loadLocalServers();
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    serverDialog.loading = false;
  }
}

async function deleteServer() {
  const def = (local.definitions || []).find(d => d.id === local.selectedId);
  const label = def && def.name ? def.name : local.selectedId;
  try {
    await ElMessageBox.confirm(
      '确定删除服务器「' + label + '」？只会从托管列表移除，不会动游戏文件本身。',
      '删除确认', { type: 'warning' });
  } catch (e) { return; }

  try {
    const r = await api('/local/servers/' + encodeURIComponent(local.selectedId), { method: 'DELETE' });
    ElMessage.success(r.response || '已删除');
    local.selectedId = '';
    await loadLocalServers();
  } catch (e) {
    ElMessage.error(e.message);
  }
}

// ---- LocalAdmin 本地功能（对应官方自带指令 lacfg / resave / help / license） ----
const localConfigDialog = reactive({ visible: false, loading: false, data: null });
const localHelpVisible = ref(false);
const localLicenseVisible = ref(false);

const localCommandMap = [
  { command: 'EXIT', official: 'Stops the server.', panel: '「停止 → 优雅停止」', implemented: true },
  { command: 'RESTART', official: 'Restarts the server.', panel: '「重启」', implemented: true },
  { command: 'FORCERESTART', official: 'Kills and restarts the server.', panel: '「强制重启」', implemented: true },
  { command: 'HBC', official: 'Cancels heartbeat restart countdown.', panel: '「中止重启倒计时」', implemented: true },
  { command: 'HBCTRL', official: 'Controls Heartbeat', panel: '「静默崩溃检测」开关 + 状态看板', implemented: true },
  { command: 'HELP', official: 'Prints all available commands.', panel: '「内置命令说明」（本窗口）', implemented: true },
  { command: 'LACFG', official: 'Prints the current LocalAdmin configuration and the configuration file path.', panel: '「查看配置」', implemented: true },
  { command: 'RESAVE', official: 'Resaves the LocalAdmin configuration file.', panel: '「重写配置文件」', implemented: true },
  { command: 'LICENSE', official: 'Prints LocalAdmin license details.', panel: '「许可与致谢」', implemented: true },
  { command: 'P', official: 'Plugin Manager.', panel: '—', implemented: false },
];

async function runGameHelp() {
  if (!local.selectedId) {
    ElMessage.warning('请先选择一个服务器');
    return;
  }
  if (!localRunning.value) {
    ElMessage.warning('该服务器未在运行，无法执行控制台命令');
    return;
  }
  try {
    const r = await api(localBase() + '/console', {
      method: 'POST', body: JSON.stringify({ command: 'help' }),
    });
    if (r.success) {
      localHelpVisible.value = false;
      ElMessage.success('已下发 help，请查看下方终端输出');
    } else {
      ElMessage.error(r.error || r.response || '下发失败');
    }
  } catch (e) {
    ElMessage.error(e.message);
  }
}

function localLevelLabel(key) {
  const m = (local.consoleLevels || []).find(o => o.key === key);
  return m ? m.label : (key || '-');
}

async function openLocalConfig() {
  localConfigDialog.visible = true;
  localConfigDialog.loading = true;
  try {
    localConfigDialog.data = await api('/local/config');
  } catch (e) {
    ElMessage.error(e.message);
    localConfigDialog.data = null;
  } finally {
    localConfigDialog.loading = false;
  }
}

async function resaveLocalConfig() {
  try {
    await ElMessageBox.confirm(
      '将把当前内存中的服务器定义规范化后写入 localadmin-servers.json。'
      + '若该文件尚不存在，会因此被创建 —— 之后配置来源将变成该文件，'
      + 'appsettings.json 里的 Servers 不再被读取。',
      '重写配置文件',
      { type: 'warning', confirmButtonText: '确定重写', cancelButtonText: '取消' });
  } catch (e) {
    return;
  }

  local.resaving = true;
  try {
    const r = await api('/local/config/resave', { method: 'POST', body: '{}' });
    if (r.success) {
      ElMessage.success(r.response || '已重写');
      await loadLocalServers();
    } else {
      ElMessage.error(r.error || '重写失败');
    }
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    local.resaving = false;
  }
}

// ---- 日志级别 ----
const logSettings = reactive({
  defaultLevel: 'Information',
  categories: [],
  levels: [],
  suggested: [],
  configPath: '',
  note: '',
  loading: false,
  saving: false,
});

async function loadLogSettings() {
  logSettings.loading = true;
  try {
    const r = await api('/logging');
    logSettings.defaultLevel = r.default || 'Information';
    logSettings.categories = (r.categories || []).map(c => ({ category: c.category, level: c.level }));
    logSettings.levels = r.levels || [];
    logSettings.suggested = r.suggestedCategories || [];
    logSettings.configPath = r.configPath || '';
    logSettings.note = r.note || '';
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    logSettings.loading = false;
  }
}

function addLogCategory() {
  logSettings.categories.push({ category: '', level: 'Debug' });
}

async function saveLogSettings() {
  const rows = logSettings.categories.filter(r => (r.category || '').trim().length > 0);
  logSettings.saving = true;
  try {
    const r = await api('/logging', {
      method: 'PUT',
      body: JSON.stringify({
        default: logSettings.defaultLevel,
        categories: rows.map(r => ({ category: r.category.trim(), level: r.level })),
      }),
    });
    ElMessage.success(r.response || '已保存');
    await loadLogSettings();
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    logSettings.saving = false;
  }
}

async function loadLocalServers() {
  local.loading = true;
  try {
    const data = await api('/local/servers');
    local.servers = data.servers || [];
    local.definitions = data.definitions || [];
    local.source = data.source || '';
    local.configPath = data.configPath || '';
    local.consoleLevels = data.consoleLevels || [];

    if (!local.servers.some(s => s.id === local.selectedId)) {
      local.selectedId = local.servers.length ? local.servers[0].id : '';
      local.lines = [];
      local.nextSeq = 0;
      local.status = null;
    }

    const match = local.servers.find(s => s.id === local.selectedId);
    if (match) {
      local.status = match;
      syncHeartbeatSwitch();
      syncConsoleLevel();
    }
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    local.loading = false;
  }
}

async function pollLocal() {
  if (page.value !== 'local' || !local.selectedId) return;
  try {
    const data = await api(localBase() + '/poll?after=' + local.nextSeq + '&limit=500');
    local.status = data.status;
    local.truncated = !!data.truncated;
    syncHeartbeatSwitch();

    const list = data.lines || [];
    if (list.length) {
      for (let i = 0; i < list.length; i++) local.lines.push(list[i]);
      local.nextSeq = list[list.length - 1].seq;
      if (local.lines.length > MAX_LOCAL_LINES) {
        local.lines.splice(0, local.lines.length - MAX_LOCAL_LINES);
      }
      if (local.autoScroll) await scrollTerminal();
    }

    const match = local.servers.find(s => s.id === local.selectedId);
    if (match) Object.assign(match, data.status);
  } catch (e) {
    // 轮询失败静默处理
  }
}

async function scrollTerminal() {
  await nextTick();
  const el = termRef.value;
  if (el) el.scrollTop = el.scrollHeight;
}

function stopLocalPolling() {
  if (localTimer) { clearInterval(localTimer); localTimer = null; }
}

function startLocalPolling() {
  stopLocalPolling();
  loadLocalServers().then(() => {
    pollLocal();
    localTimer = setInterval(pollLocal, 1000);
  });
}

async function refreshLocal() {
  await loadLocalServers();
  await pollLocal();
  ElMessage.success('已刷新');
}

async function onLocalServerChange() {
  local.lines = [];
  local.nextSeq = 0;
  local.status = null;
  await pollLocal();
  syncConsoleLevel();
  await scrollTerminal();
}

async function localPost(path, body) {
  try {
    const res = await api(localBase() + path, { method: 'POST', body: JSON.stringify(body || {}) });
    if (res.success) ElMessage.success(res.response || '操作已下发');
    else ElMessage.error(res.error || res.response || '操作失败');
  } catch (e) {
    ElMessage.error(e.message);
  }
}

async function localStart() {
  await localPost('/start', {});
  await pollLocal();
}

async function localStop(mode) {
  const force = mode === 'force';
  const action = force ? '强制结束进程树' : '优雅停止（下发 exit 并等待）';
  const serverName = (local.status && local.status.name) ? local.status.name : '当前服务器';

  try {
    await ElMessageBox.confirm(
      `<div style="line-height:1.7;">
        <p style="font-weight:bold;color:#f56c6c;font-size:15px;margin-bottom:8px;">⚠️ 严重警告：关闭程序将导致服务器直接断开下线！</p>
        <p>确定要对【<b>${serverName}</b>】执行「<b>${action}</b>」吗？</p>
        <div style="color:#e6a23c;margin-top:8px;background:rgba(230,162,60,0.12);padding:10px;border-radius:6px;border-left:4px solid #e6a23c;">
          <b>【操作后果提示】</b><br/>
          <div style="background:#e53e3e;color:#ffffff;padding:8px 10px;border-radius:4px;font-weight:bold;margin:6px 0;">
            🚨 1. 游戏服务端进程将被立即终止，所有在线玩家将立即全部掉线！
          </div>
          2. 正在进行的游戏对局数据可能无法完整保存；<br/>
          3. 服务器将从在线列表中移除，外部玩家无法再连接。
        </div>
      </div>`,
      '关闭程序高危警告 (第 1 次确认 / 共 2 次)',
      {
        dangerouslyUseHTMLString: true,
        type: 'error',
        confirmButtonText: '我已知晓玩家将全部掉线，下一步',
        cancelButtonText: '取消操作',
        confirmButtonClass: 'el-button--danger'
      }
    );
  } catch (e) { return; }

  try {
    await ElMessageBox.confirm(
      `<div style="line-height:1.7;">
        <p style="font-weight:bold;color:#f56c6c;font-size:15px;margin-bottom:8px;">🚨 最终二次确认：真的要立即关闭服务器吗？</p>
        <p>请再次确认：您即将彻底停止【<b>${serverName}</b>】进程！</p>
        <p style="color:#f56c6c;margin-top:6px;font-weight:bold;">
          💥 点击确认后，该服务器将立即停止，所有在线玩家全部掉线！
        </p>
        <p style="color:#909399;font-size:12px;margin-top:6px;">
          （若只是想更换地图或重开对局，请点取消并使用「重启」或游戏内指令）
        </p>
      </div>`,
      '关闭程序最终确认 (第 2 次确认 / 共 2 次)',
      {
        dangerouslyUseHTMLString: true,
        type: 'error',
        confirmButtonText: '确认立即关闭，使全员掉线',
        cancelButtonText: '放弃关闭',
        confirmButtonClass: 'el-button--danger'
      }
    );
  } catch (e) { return; }

  await localPost('/stop', { force });
  await pollLocal();
}

async function localRestart(force) {
  const serverName = (local.status && local.status.name) ? local.status.name : '当前服务器';
  const action = force ? '强制重启（立即杀掉进程树并拉起）' : '平滑重启（下发 exit 等待退出后重新拉起）';
  try {
    await ElMessageBox.confirm(
      `<div style="line-height:1.7;">
        <p style="font-weight:bold;color:#e6a23c;font-size:15px;margin-bottom:8px;">⚠️ 重启警告：重启服务器将导致在线玩家掉线！</p>
        <p>确定要对【<b>${serverName}</b>】执行「<b>${action}</b>」吗？</p>
        <div style="color:#f56c6c;margin-top:8px;background:rgba(245,108,108,0.1);padding:10px;border-radius:6px;border-left:4px solid #f56c6c;">
          <b>提示：</b>对局将被强制终止，<b>所有正在游玩的玩家将全部断开掉线</b>，直至服务端重新启动完毕。
        </div>
      </div>`,
      '重启服务器确认',
      {
        dangerouslyUseHTMLString: true,
        type: 'warning',
        confirmButtonText: '确认重启（玩家将掉线）',
        cancelButtonText: '取消',
        confirmButtonClass: 'el-button--warning'
      }
    );
  } catch (e) { return; }
  await localPost('/restart', { force: !!force });
  await pollLocal();
}

async function localCancelRestart() {
  await localPost('/cancel-restart', {});
  await pollLocal();
}

async function localToggleHeartbeat(value) {
  await localPost('/heartbeat', { enabled: !!value });
  await pollLocal();
}

async function localClearConsole() {
  try {
    await api(localBase() + '/console/clear', { method: 'POST', body: JSON.stringify({}) });
    local.lines = [];
    local.nextSeq = 0;
    ElMessage.success('控制台已清空');
  } catch (e) {
    ElMessage.error(e.message);
  }
}

async function localSendCommand() {
  const cmd = local.command.trim();
  if (!cmd) return;

  local.sending = true;
  try {
    const res = await api(localBase() + '/console', {
      method: 'POST',
      body: JSON.stringify({ command: cmd })
    });
    if (res.success) local.command = '';
    else ElMessage.error(res.error || res.response || '下发失败');
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    local.sending = false;
    await pollLocal();
  }
}

function localStatusText(s) {
  if (!s) return '未知';
  if (s.running) return '运行中';
  if (s.restartBudgetExhausted) return '已停止（重启限流）';
  return '已停止';
}

function localStatusType(s) {
  if (!s) return 'info';
  if (s.running) return 'success';
  if (s.restartBudgetExhausted) return 'danger';
  return 'info';
}

function heartbeatText(s) {
  if (!s) return '-';
  if (!s.heartbeatEnabled) return '未启用（启动未带 -heartbeat）';
  if (s.heartbeatStatus === 'active') return '监控中';
  if (s.heartbeatStatus === 'awaiting') return '等待首个心跳';
  return '已关闭';
}

function lastHeartbeatText(s) {
  if (!s || !s.lastHeartbeatAt) return '-';
  const d = new Date(s.lastHeartbeatAt);
  if (isNaN(d.getTime())) return '-';
  const diff = Math.max(0, Math.round((Date.now() - d.getTime()) / 1000));
  return diff + ' 秒前';
}

function formatUptime(seconds) {
  const total = Math.floor(seconds || 0);
  if (total <= 0) return '-';
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  if (h > 0) return h + ' 时 ' + m + ' 分 ' + s + ' 秒';
  if (m > 0) return m + ' 分 ' + s + ' 秒';
  return s + ' 秒';
}

function timeOnly(v) {
  if (!v) return '';
  const d = new Date(v);
  return isNaN(d.getTime()) ? '' : d.toLocaleTimeString('zh-CN', { hour12: false });
}

function lineLabel(kind) {
  switch (kind) {
    case 'input': return '命令';
    case 'system': return '面板';
    case 'stdout': return '输出';
    case 'stderr': return '错误';
    case 'control': return '控制';
    default: return '';
  }
}

export function useLocalAdmin() {
  return {
    local,
    termRef,
    localRunning,
    loadLocalServers,
    pollLocal,
    stopLocalPolling,
    startLocalPolling,
    refreshLocal,
    onLocalServerChange,
    localStart,
    localStop,
    localRestart,
    localCancelRestart,
    localToggleHeartbeat,
    localClearConsole,
    localSendCommand,
    localSetConsoleLevel,
    serverDialog,
    openServerDialog,
    saveServer,
    deleteServer,
    discoverExecutable,
    logSettings,
    loadLogSettings,
    saveLogSettings,
    addLogCategory,
    localConfigDialog,
    openLocalConfig,
    resaveLocalConfig,
    localHelpVisible,
    localLicenseVisible,
    localCommandMap,
    localLevelLabel,
    runGameHelp,
    localStatusText,
    localStatusType,
    heartbeatText,
    lastHeartbeatText,
    formatUptime,
    timeOnly,
    lineLabel,
  };
}
