import { reactive } from '../deps.js';
import { api, session } from '../api.js';
import { ElMessage, ElMessageBox } from '../deps.js';

const db = reactive({
  activeTab: 'players',
  loadingStatus: false,
  saving: false,
  testing: false,
  initializing: false,
  status: { connected: false, summary: null },
  testResult: null,
});

const dbForm = reactive({
  connectionString: '',
});

const playerQuery = reactive({
  search: '',
  page: 1,
  pageSize: 20,
});

const playerState = reactive({
  loading: false,
  items: [],
  total: 0,
});

const playerDialog = reactive({
  visible: false,
  isEdit: false,
  saving: false,
  form: {
    id: '',
    playerName: '',
    qqId: null,
    isAdmin: false,
    adminNote: '',
    playTimeSeconds: 0,
    playersKilled: 0,
    scpsKilled: 0,
    deaths: 0,
  }
});

const rankingDialog = reactive({
  visible: false,
  loading: false,
  metric: 'playtime',
  items: [],
});

const banQuery = reactive({
  search: '',
  activeOnly: false,
  page: 1,
  pageSize: 20,
});

const banState = reactive({
  loading: false,
  items: [],
  total: 0,
});

const banDialog = reactive({
  visible: false,
  isEdit: false,
  saving: false,
  durationPreset: -1,
  form: {
    id: '',
    playerIP: '0.0.0.0',
    unbanTime: null,
    adminId: '',
    adminName: '',
    reason: '',
  }
});

function formatPlayTime(seconds) {
  const s = Number(seconds) || 0;
  if (s <= 0) return '0 分';
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  if (h > 0) return h + ' 小时 ' + m + ' 分';
  return m + ' 分';
}

async function loadDbStatus() {
  db.loadingStatus = true;
  try {
    const res = await api('/db/status');
    db.status.connected = res.summary && !res.summary.error;
    db.status.summary = res.summary || {};
    dbForm.connectionString = res.connectionString || '';
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    db.loadingStatus = false;
  }
}

async function saveDbConfig() {
  db.saving = true;
  try {
    const res = await api('/db/config', {
      method: 'POST',
      body: JSON.stringify({ connectionString: dbForm.connectionString.trim(), testFirst: false })
    });
    ElMessage.success(res.message || '配置已保存');
    await loadDbStatus();
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    db.saving = false;
  }
}

async function testDbConnection() {
  db.testing = true;
  db.testResult = null;
  try {
    const res = await api('/db/test-connection', {
      method: 'POST',
      body: JSON.stringify({ connectionString: dbForm.connectionString.trim() })
    });
    if (res.success) {
      db.testResult = {
        success: true,
        title: `连接成功 (耗时 ${res.elapsedMs}ms)`,
        detail: `表检测：PlayerData ${res.playerDataExists ? '已存在' : '不存在'}，BanPlayerData ${res.banPlayerDataExists ? '已存在' : '不存在'}`
      };
      ElMessage.success('连接测试成功');
    } else {
      db.testResult = {
        success: false,
        title: '连接测试失败',
        detail: res.error || '未知错误'
      };
      ElMessage.error('连接失败：' + (res.error || ''));
    }
  } catch (e) {
    db.testResult = {
      success: false,
      title: '连接测试失败',
      detail: e.message
    };
    ElMessage.error(e.message);
  } finally {
    db.testing = false;
  }
}

async function initDbTables() {
  try {
    await ElMessageBox.confirm('确定要在当前 MySQL 数据库中初始化 PlayerData 与 BanPlayerData 数据表吗？', '初始化数据表', {
      type: 'warning',
      confirmButtonText: '确定初始化',
      cancelButtonText: '取消'
    });
  } catch {
    return;
  }

  db.initializing = true;
  try {
    const res = await api('/db/init-tables', { method: 'POST' });
    ElMessage.success(res.message || '表结构初始化成功');
    await loadDbStatus();
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    db.initializing = false;
  }
}

async function loadPlayers(page) {
  if (typeof page === 'number') playerQuery.page = page;
  playerState.loading = true;
  try {
    const q = new URLSearchParams({
      page: playerQuery.page,
      pageSize: playerQuery.pageSize,
    });
    if (playerQuery.search.trim()) q.set('search', playerQuery.search.trim());
    const res = await api('/db/players?' + q.toString());
    playerState.items = res.items || [];
    playerState.total = res.total || 0;
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    playerState.loading = false;
  }
}

function onPlayerPageChange(p) {
  playerQuery.page = p;
  loadPlayers();
}

function onPlayerSizeChange(s) {
  playerQuery.pageSize = s;
  loadPlayers(1);
}

function openPlayerDialog(row) {
  playerDialog.isEdit = !!row;
  if (row) {
    playerDialog.form = {
      id: row.id,
      playerName: row.playerName || '',
      qqId: row.qqId || null,
      isAdmin: !!row.isAdmin,
      adminNote: row.adminNote || '',
      playTimeSeconds: row.playTimeSeconds || 0,
      playersKilled: row.playersKilled || 0,
      scpsKilled: row.scpsKilled || 0,
      deaths: row.deaths || 0,
    };
  } else {
    playerDialog.form = {
      id: '',
      playerName: '',
      qqId: null,
      isAdmin: false,
      adminNote: '',
      playTimeSeconds: 0,
      playersKilled: 0,
      scpsKilled: 0,
      deaths: 0,
    };
  }
  playerDialog.visible = true;
}

async function savePlayer() {
  if (!playerDialog.form.id.trim()) {
    ElMessage.warning('玩家 ID 不能为空');
    return;
  }
  if (!playerDialog.form.playerName.trim()) {
    ElMessage.warning('玩家昵称不能为空');
    return;
  }

  playerDialog.saving = true;
  try {
    const res = await api('/db/players', {
      method: 'POST',
      body: JSON.stringify({
        id: playerDialog.form.id.trim(),
        playerName: playerDialog.form.playerName.trim(),
        qqId: playerDialog.form.qqId ? Number(playerDialog.form.qqId) : null,
        isAdmin: playerDialog.form.isAdmin,
        adminNote: playerDialog.form.adminNote ? playerDialog.form.adminNote.trim() : null,
        playTimeSeconds: Number(playerDialog.form.playTimeSeconds) || 0,
        playersKilled: Number(playerDialog.form.playersKilled) || 0,
        scpsKilled: Number(playerDialog.form.scpsKilled) || 0,
        deaths: Number(playerDialog.form.deaths) || 0,
      })
    });
    ElMessage.success(res.message || '玩家数据已保存');
    playerDialog.visible = false;
    await loadPlayers();
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    playerDialog.saving = false;
  }
}

async function deletePlayer(row) {
  try {
    await ElMessageBox.confirm(`确定删除玩家【${row.playerName}】(${row.id}) 的统计数据吗？此操作不可逆。`, '删除玩家', {
      type: 'error',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    });
  } catch {
    return;
  }

  try {
    await api('/db/players/' + encodeURIComponent(row.id), { method: 'DELETE' });
    ElMessage.success('玩家数据已删除');
    await loadPlayers();
  } catch (e) {
    ElMessage.error(e.message);
  }
}

function openRankingDialog() {
  rankingDialog.metric = 'playtime';
  rankingDialog.visible = true;
  loadRankings();
}

async function loadRankings() {
  rankingDialog.loading = true;
  try {
    const res = await api('/db/players/rankings?metric=' + rankingDialog.metric + '&limit=20');
    rankingDialog.items = res.items || [];
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    rankingDialog.loading = false;
  }
}

async function loadBans(page) {
  if (typeof page === 'number') banQuery.page = page;
  banState.loading = true;
  try {
    const q = new URLSearchParams({
      page: banQuery.page,
      pageSize: banQuery.pageSize,
    });
    if (banQuery.search.trim()) q.set('search', banQuery.search.trim());
    if (banQuery.activeOnly) q.set('activeOnly', 'true');
    const res = await api('/db/bans?' + q.toString());
    banState.items = res.items || [];
    banState.total = res.total || 0;
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    banState.loading = false;
  }
}

function onBanPageChange(p) {
  banQuery.page = p;
  loadBans();
}

function onBanSizeChange(s) {
  banQuery.pageSize = s;
  loadBans(1);
}

function onBanPresetChange(val) {
  if (val === -1) {
    banDialog.form.unbanTime = null;
  } else if (val > 0) {
    banDialog.form.unbanTime = new Date(Date.now() + val * 1000);
  }
}

function openBanDialog(row) {
  banDialog.isEdit = !!row;
  if (row) {
    banDialog.durationPreset = row.isPermanent ? -1 : 0;
    banDialog.form = {
      id: row.id,
      playerIP: row.playerIP || '0.0.0.0',
      unbanTime: row.isPermanent ? null : new Date(row.unbanTime),
      adminId: row.adminId || '',
      adminName: row.adminName || '',
      reason: row.reason || '',
    };
  } else {
    banDialog.durationPreset = -1;
    banDialog.form = {
      id: '',
      playerIP: '0.0.0.0',
      unbanTime: null,
      adminId: (session.value && session.value.username) || '',
      adminName: (session.value && session.value.displayName) || '',
      reason: '',
    };
  }
  banDialog.visible = true;
}

async function saveBan() {
  if (!banDialog.form.id.trim()) {
    ElMessage.warning('封禁目标 ID 不能为空');
    return;
  }

  banDialog.saving = true;
  try {
    let unbanTimeStr = null;
    let durationSeconds = null;

    if (banDialog.durationPreset === -1) {
      durationSeconds = null; // permanent
    } else if (banDialog.durationPreset > 0) {
      durationSeconds = banDialog.durationPreset;
    } else if (banDialog.form.unbanTime) {
      unbanTimeStr = new Date(banDialog.form.unbanTime).toISOString();
    }

    const res = await api('/db/bans', {
      method: 'POST',
      body: JSON.stringify({
        id: banDialog.form.id.trim(),
        playerIP: banDialog.form.playerIP.trim(),
        unbanTime: unbanTimeStr,
        durationSeconds: durationSeconds,
        adminId: banDialog.form.adminId.trim(),
        adminName: banDialog.form.adminName.trim(),
        reason: banDialog.form.reason.trim(),
      })
    });
    ElMessage.success(res.message || '封禁记录已保存');
    banDialog.visible = false;
    await loadBans();
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    banDialog.saving = false;
  }
}

async function deleteBan(row, actionType) {
  const isUnban = actionType === 'unban';
  try {
    await ElMessageBox.confirm(
      isUnban ? `确定为玩家【${row.id}】解除封禁吗？` : `确定删除封禁记录【${row.id}】吗？`,
      isUnban ? '解除封禁' : '删除记录',
      {
        type: isUnban ? 'warning' : 'error',
        confirmButtonText: isUnban ? '确认解封' : '确认删除',
        cancelButtonText: '取消'
      }
    );
  } catch {
    return;
  }

  try {
    await api('/db/bans/' + encodeURIComponent(row.id), { method: 'DELETE' });
    ElMessage.success(isUnban ? '已解除封禁' : '记录已删除');
    await loadBans();
  } catch (e) {
    ElMessage.error(e.message);
  }
}

function onDbTabChange(tab) {
  if (tab === 'players') loadPlayers();
  else if (tab === 'bans') loadBans();
  else if (tab === 'config') loadDbStatus();
}

export function useDatabase() {
  return {
    db,
    dbForm,
    playerQuery,
    playerState,
    playerDialog,
    rankingDialog,
    banQuery,
    banState,
    banDialog,
    formatPlayTime,
    loadDbStatus,
    saveDbConfig,
    testDbConnection,
    initDbTables,
    loadPlayers,
    onPlayerPageChange,
    onPlayerSizeChange,
    openPlayerDialog,
    savePlayer,
    deletePlayer,
    openRankingDialog,
    loadRankings,
    loadBans,
    onBanPageChange,
    onBanSizeChange,
    onBanPresetChange,
    openBanDialog,
    saveBan,
    deleteBan,
    onDbTabChange,
  };
}
