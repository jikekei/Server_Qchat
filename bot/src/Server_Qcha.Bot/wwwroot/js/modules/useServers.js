import { ref, reactive } from '../deps.js';
import { api } from '../api.js';
import { ElMessage, ElMessageBox } from '../deps.js';

const servers = ref([]);
const loadingServers = ref(false);

async function loadServers() {
  loadingServers.value = true;
  try {
    const data = await api('/servers');
    servers.value = data.servers || [];
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    loadingServers.value = false;
  }
}

// ---- 玩家列表 ----
const playersDialog = reactive({ visible: false, loading: false, title: '', server: null, players: [] });

async function openPlayers(server) {
  playersDialog.server = server;
  playersDialog.title = '玩家列表 · ' + server.name;
  playersDialog.players = [];
  playersDialog.visible = true;
  await reloadPlayers();
}

async function reloadPlayers() {
  if (!playersDialog.server) return;
  playersDialog.loading = true;
  try {
    const data = await api('/servers/' + playersDialog.server.index + '/players');
    const rawList = data.players || [];
    playersDialog.players = rawList.filter(p => {
      const n = (p.name || '').toLowerCase();
      return n !== 'dedicated server' && !n.startsWith('dedicated server@');
    });
  } catch (e) {
    ElMessage.error(e.message);
    playersDialog.players = [];
  } finally {
    playersDialog.loading = false;
  }
}

// ---- 服务器详情 ----
const infoDialog = reactive({ visible: false, title: '', text: '' });

async function openInfo(server) {
  infoDialog.title = '服务器详情 · ' + server.name;
  infoDialog.text = '';
  infoDialog.visible = true;
  try {
    const data = await api('/servers/' + server.index + '/info');
    infoDialog.text = data.raw || '';
  } catch (e) {
    infoDialog.text = '获取失败：' + e.message;
  }
}

// ---- 回合控制 ----
async function doRound(server, mode) {
  const action = mode === 'start' ? '强制开始回合' : '重启回合';
  try {
    await ElMessageBox.confirm(
      '确定要在【' + server.name + '】' + action + '吗？',
      '操作确认',
      { type: 'warning', confirmButtonText: '确定', cancelButtonText: '取消' }
    );
  } catch (e) {
    return;
  }

  try {
    const path = mode === 'start' ? '/round/start' : '/round/restart';
    const res = await api('/servers/' + server.index + path, { method: 'POST' });
    if (res.success) ElMessage.success(res.response || '操作已下发');
    else ElMessage.error(res.error || res.response || '操作失败');
  } catch (e) {
    ElMessage.error(e.message);
  }
}

// ---- 广播 ----
const broadcastDialog = reactive({ visible: false, loading: false, title: '', server: null, message: '' });

function openBroadcast(server) {
  broadcastDialog.server = server;
  broadcastDialog.title = '发送广播 · ' + server.name;
  broadcastDialog.message = '';
  broadcastDialog.visible = true;
}

async function submitBroadcast() {
  if (!broadcastDialog.message.trim()) {
    ElMessage.warning('广播内容不能为空');
    return;
  }
  broadcastDialog.loading = true;
  try {
    const res = await api('/servers/' + broadcastDialog.server.index + '/broadcast', {
      method: 'POST',
      body: JSON.stringify({ message: broadcastDialog.message.trim() })
    });
    if (res.success) {
      ElMessage.success(res.response || '广播已发送');
      broadcastDialog.visible = false;
    } else {
      ElMessage.error(res.error || res.response || '发送失败');
    }
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    broadcastDialog.loading = false;
  }
}

// ---- 踢出 / 封禁 ----
const durationOptions = [
  { label: '5 分钟', value: 300 },
  { label: '30 分钟', value: 1800 },
  { label: '1 小时', value: 3600 },
  { label: '6 小时', value: 21600 },
  { label: '1 天', value: 86400 },
  { label: '7 天', value: 604800 },
  { label: '30 天', value: 2592000 },
  { label: '永久（10 年）', value: 315360000 },
];

const punishDialog = reactive({
  visible: false, loading: false, title: '', mode: 'kick',
  server: null, playerId: '', playerName: '', duration: 3600, reason: ''
});

function openPunish(mode, player) {
  punishDialog.mode = mode;
  punishDialog.playerId = player.id;
  punishDialog.playerName = player.name;
  punishDialog.duration = mode === 'kick' ? 300 : 3600;
  punishDialog.reason = mode === 'kick' ? '被管理员踢出' : '违反服务器规则';
  punishDialog.title = (mode === 'kick' ? '踢出玩家' : '封禁玩家') + ' · ' + playersDialog.server.name;
  punishDialog.visible = true;
}

async function submitPunish() {
  punishDialog.loading = true;
  try {
    const res = await api('/servers/' + playersDialog.server.index + '/' + punishDialog.mode, {
      method: 'POST',
      body: JSON.stringify({
        playerId: punishDialog.playerId,
        duration: punishDialog.duration,
        reason: punishDialog.reason
      })
    });
    if (res.success) {
      ElMessage.success(res.response || '操作成功');
      punishDialog.visible = false;
      await reloadPlayers();
    } else {
      ElMessage.error(res.error || res.response || '操作失败');
    }
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    punishDialog.loading = false;
  }
}

export function useServers() {
  return {
    servers,
    loadingServers,
    loadServers,
    playersDialog,
    openPlayers,
    reloadPlayers,
    infoDialog,
    openInfo,
    doRound,
    broadcastDialog,
    openBroadcast,
    submitBroadcast,
    durationOptions,
    punishDialog,
    openPunish,
    submitPunish,
  };
}
