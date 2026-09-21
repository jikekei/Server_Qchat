import { reactive } from '../deps.js';
import { api } from '../api.js';
import { ElMessage } from '../deps.js';

const bot = reactive({
  status: {},
  groups: [],
  friends: [],
  configPath: '',
  loading: false,
  reconnecting: false,
  saving: false,
  testingAuth: false,
  hasClientSecret: false,
  panels: [],
  loadingPanels: false,
  syncingPanels: false,
  deletingPanelId: null,
});

const botForm = reactive({
  mode: 'NapCat',
  wsBaseUri: '',
  reconnectDelaySeconds: 5,
  maxReconnectDelaySeconds: 30,
  allowedGroupIds: [],
  notifyGroupIds: [],
  notifyPrivateUserIds: [],
  acTargetGroupId: 0,

  // ---- QQ 官方 Bot API ----
  officialApiBase: 'https://api.bot.qq.com',
  officialAppId: '',
  officialClientSecret: '',
  officialSandbox: false,
  officialIntents: 33554432,
  officialMaxTextLength: 800,
  officialAllowActivePush: false,
  officialAdminOpenIds: [],
  officialAllowedGroupOpenIds: [],
  officialNotifyGroupOpenIds: [],
  officialNotifyPrivateOpenIds: [],
  officialAcTargetGroupOpenId: '',
});

const testDialog = reactive({
  visible: false,
  loading: false,
  isGroup: true,
  targetId: '',
  message: '这是一条来自 Qridge Web 面板的测试消息',
});

async function loadBotStatus() {
  bot.loading = true;
  try {
    const data = await api('/bot/status');
    bot.status = data.status || {};
    bot.groups = data.groups || [];
    bot.friends = data.friends || [];
    bot.configPath = data.configPath || '';

    const s = data.settings || {};
    botForm.mode = s.mode || 'NapCat';
    botForm.wsBaseUri = s.wsBaseUri || 'ws://127.0.0.1:6700';
    botForm.reconnectDelaySeconds = s.reconnectDelaySeconds || 5;
    botForm.maxReconnectDelaySeconds = s.maxReconnectDelaySeconds || 30;
    botForm.allowedGroupIds = s.allowedGroupIds || [];
    botForm.notifyGroupIds = s.notifyGroupIds || [];
    botForm.notifyPrivateUserIds = s.notifyPrivateUserIds || [];
    botForm.acTargetGroupId = s.acTargetGroupId || 0;

    const o = s.officialQq || {};
    bot.hasClientSecret = !!o.hasClientSecret;
    botForm.officialApiBase = o.apiBase || 'https://api.bot.qq.com';
    botForm.officialAppId = o.appId || '';
    // 密钥不回显：留空表示保持服务端已保存的值不变
    botForm.officialClientSecret = '';
    botForm.officialSandbox = !!o.sandbox;
    botForm.officialIntents = o.intents || 33554432;
    botForm.officialMaxTextLength = o.maxTextLength || 800;
    botForm.officialAllowActivePush = !!o.allowActivePush;
    botForm.officialAdminOpenIds = o.adminOpenIds || [];
    botForm.officialAllowedGroupOpenIds = o.allowedGroupOpenIds || [];
    botForm.officialNotifyGroupOpenIds = o.notifyGroupOpenIds || [];
    botForm.officialNotifyPrivateOpenIds = o.notifyPrivateOpenIds || [];
    botForm.officialAcTargetGroupOpenId = o.acTargetGroupOpenId || '';

    if (botForm.mode === 'OfficialQq' && botForm.officialAppId) {
      loadOfficialPanels();
    }
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    bot.loading = false;
  }
}

async function reconnectBot() {
  bot.reconnecting = true;
  try {
    const res = await api('/bot/reconnect', { method: 'POST' });
    ElMessage.success(res.message || '已发起重新连接');
    await loadBotStatus();
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    bot.reconnecting = false;
  }
}

async function saveBotSettings() {
  const official = botForm.mode === 'OfficialQq';

  if (!official && (!botForm.wsBaseUri || (!botForm.wsBaseUri.startsWith('ws://') && !botForm.wsBaseUri.startsWith('wss://')))) {
    ElMessage.warning('WebSocket 地址必须以 ws:// 或 wss:// 开头');
    return;
  }

  if (official) {
    if (!botForm.officialAppId || !botForm.officialAppId.trim()) {
      ElMessage.warning('官方模式需要填写 AppID');
      return;
    }
    if (!bot.hasClientSecret && !botForm.officialClientSecret) {
      ElMessage.warning('官方模式需要填写 AppSecret');
      return;
    }
  }

  bot.saving = true;
  try {
    const res = await api('/bot/settings', {
      method: 'PUT',
      body: JSON.stringify({
        mode: botForm.mode,
        wsBaseUri: botForm.wsBaseUri.trim(),
        reconnectDelaySeconds: botForm.reconnectDelaySeconds,
        maxReconnectDelaySeconds: botForm.maxReconnectDelaySeconds,
        allowedGroupIds: botForm.allowedGroupIds.map(Number).filter(n => n > 0),
        notifyGroupIds: botForm.notifyGroupIds.map(Number).filter(n => n > 0),
        notifyPrivateUserIds: botForm.notifyPrivateUserIds.map(Number).filter(n => n > 0),
        acTargetGroupId: Number(botForm.acTargetGroupId) || 0,
        autoReconnect: true,

        officialApiBase: botForm.officialApiBase.trim(),
        officialAppId: botForm.officialAppId.trim(),
        officialClientSecret: botForm.officialClientSecret,
        officialSandbox: botForm.officialSandbox,
        officialIntents: Number(botForm.officialIntents) || 33554432,
        officialMaxTextLength: Number(botForm.officialMaxTextLength) || 800,
        officialAllowActivePush: botForm.officialAllowActivePush,
        officialAdminOpenIds: normalizeList(botForm.officialAdminOpenIds),
        officialAllowedGroupOpenIds: normalizeList(botForm.officialAllowedGroupOpenIds),
        officialNotifyGroupOpenIds: normalizeList(botForm.officialNotifyGroupOpenIds),
        officialNotifyPrivateOpenIds: normalizeList(botForm.officialNotifyPrivateOpenIds),
        officialAcTargetGroupOpenId: (botForm.officialAcTargetGroupOpenId || '').trim(),
      })
    });
    ElMessage.success(res.message || '设置已保存');

    // 配置热重载是异步的（文件监视 → 配置重载 → 触发模式切换）。
    // 如果保存后立刻回读，拿到的是旧配置，界面会把刚选的模式"弹"回去，用户以为没保存成功。
    // 这里先按响应里的期望值收敛本地状态，再轮询回读直到一致（或超时）。
    const expectMode = res.mode;
    if (expectMode) {
      botForm.mode = expectMode;
      const deadline = Date.now() + 4000;
      do {
        await new Promise(r => setTimeout(r, 300));
        await loadBotStatus();
      } while (botForm.mode !== expectMode && Date.now() < deadline);
    } else {
      await loadBotStatus();
    }
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    bot.saving = false;
  }
}

/**
 * 校验官方开放平台凭证（只申请一次 AccessToken，不改模式、不落盘）。
 * AppSecret 留空时后端会用已保存的密钥，方便"只想确认已存的密钥还有效"。
 */
async function testOfficialAuth() {
  bot.testingAuth = true;
  try {
    const res = await api('/bot/test-official-auth', {
      method: 'POST',
      body: JSON.stringify({
        apiBase: botForm.officialApiBase.trim(),
        appId: botForm.officialAppId.trim(),
        clientSecret: botForm.officialClientSecret,
        sandbox: botForm.officialSandbox,
      })
    });

    if (res.success) {
      ElMessage.success(res.message || '凭证有效');
    } else {
      ElMessage.error(res.message || '凭证校验失败');
    }
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    bot.testingAuth = false;
  }
}

function normalizeList(list) {
  return (list || [])
    .map(v => String(v).trim())
    .filter(v => v.length > 0)
    .filter((v, i, arr) => arr.indexOf(v) === i);
}

function addAllowedGroup(groupId) {
  if (!botForm.allowedGroupIds.includes(groupId)) {
    botForm.allowedGroupIds.push(groupId);
    ElMessage.success('已添加到白名单（记得点击下方保存设置）');
  }
}

function addOfficialGroupOpenId(openId) {
  if (!openId) return;
  if (!botForm.officialAllowedGroupOpenIds.includes(openId)) {
    botForm.officialAllowedGroupOpenIds.push(openId);
    ElMessage.success('已添加到官方群白名单（记得点击下方保存设置）');
  }
}

function addOfficialNotifyGroup(openId) {
  if (!openId) return;
  if (!botForm.officialNotifyGroupOpenIds.includes(openId)) {
    botForm.officialNotifyGroupOpenIds.push(openId);
    ElMessage.success('已添加为通知群（记得点击下方保存设置）');
  }
}

function openTestDialog() {
  testDialog.isGroup = true;
  testDialog.targetId = bot.groups.length > 0 ? bot.groups[0].groupId : '';
  testDialog.message = '这是一条来自 Qridge Web 面板的测试消息';
  testDialog.visible = true;
}

async function submitTestMessage() {
  const targetId = String(testDialog.targetId || '').trim();
  if (!targetId) {
    ElMessage.warning('请选择或输入目标 ID');
    return;
  }
  if (!testDialog.message.trim()) {
    ElMessage.warning('消息内容不能为空');
    return;
  }

  testDialog.loading = true;
  try {
    const res = await api('/bot/send-test', {
      method: 'POST',
      body: JSON.stringify({
        isGroup: testDialog.isGroup,
        targetId,
        message: testDialog.message.trim(),
      })
    });
    if (res.success) {
      ElMessage.success(res.message || '测试消息发送成功');
      testDialog.visible = false;
    } else {
      ElMessage.error(res.error || '发送失败');
    }
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    testDialog.loading = false;
  }
}

let commandManifest = '';

async function loadCommandManifest() {
  try {
    const data = await api('/bot/commands');
    commandManifest = data.officialConsoleManifest || '';
  } catch (e) {
    ElMessage.error(e.message);
  }
  return commandManifest;
}

async function loadOfficialPanels() {
  bot.loadingPanels = true;
  try {
    const data = await api('/bot/official/panels');
    bot.panels = data.panels || [];
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    bot.loadingPanels = false;
  }
}

async function syncOfficialPanels() {
  bot.syncingPanels = true;
  try {
    const res = await api('/bot/official/panels/sync', { method: 'POST' });
    if (res.success) {
      ElMessage.success(res.message || '指令面板已成功同步到 QQ 开放平台');
      await loadOfficialPanels();
    } else {
      ElMessage.error(res.message || '同步指令面板失败');
    }
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    bot.syncingPanels = false;
  }
}

async function deleteOfficialPanel(panelId) {
  if (!panelId) return;
  bot.deletingPanelId = panelId;
  try {
    const res = await api(`/bot/official/panels/${encodeURIComponent(panelId)}`, { method: 'DELETE' });
    if (res.success) {
      ElMessage.success(res.message || '指令面板已删除');
      await loadOfficialPanels();
    } else {
      ElMessage.error(res.message || '删除失败');
    }
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    bot.deletingPanelId = null;
  }
}

export function useBot() {
  return {
    bot,
    botForm,
    testDialog,
    loadBotStatus,
    reconnectBot,
    saveBotSettings,
    testOfficialAuth,
    addAllowedGroup,
    addOfficialGroupOpenId,
    addOfficialNotifyGroup,
    openTestDialog,
    submitTestMessage,
    loadCommandManifest,
    loadOfficialPanels,
    syncOfficialPanels,
    deleteOfficialPanel,
  };
}

