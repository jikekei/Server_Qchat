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
});

const botForm = reactive({
  wsBaseUri: '',
  reconnectDelaySeconds: 5,
  maxReconnectDelaySeconds: 30,
  allowedGroupIds: [],
  notifyGroupIds: [],
  notifyPrivateUserIds: [],
  acTargetGroupId: 0,
});

const testDialog = reactive({
  visible: false,
  loading: false,
  isGroup: true,
  targetId: null,
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
    botForm.wsBaseUri = s.wsBaseUri || 'ws://127.0.0.1:6700';
    botForm.reconnectDelaySeconds = s.reconnectDelaySeconds || 5;
    botForm.maxReconnectDelaySeconds = s.maxReconnectDelaySeconds || 30;
    botForm.allowedGroupIds = s.allowedGroupIds || [];
    botForm.notifyGroupIds = s.notifyGroupIds || [];
    botForm.notifyPrivateUserIds = s.notifyPrivateUserIds || [];
    botForm.acTargetGroupId = s.acTargetGroupId || 0;
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
  if (!botForm.wsBaseUri || (!botForm.wsBaseUri.startsWith('ws://') && !botForm.wsBaseUri.startsWith('wss://'))) {
    ElMessage.warning('WebSocket 地址必须以 ws:// 或 wss:// 开头');
    return;
  }

  bot.saving = true;
  try {
    const res = await api('/bot/settings', {
      method: 'PUT',
      body: JSON.stringify({
        wsBaseUri: botForm.wsBaseUri.trim(),
        reconnectDelaySeconds: botForm.reconnectDelaySeconds,
        maxReconnectDelaySeconds: botForm.maxReconnectDelaySeconds,
        allowedGroupIds: botForm.allowedGroupIds.map(Number).filter(n => n > 0),
        notifyGroupIds: botForm.notifyGroupIds.map(Number).filter(n => n > 0),
        notifyPrivateUserIds: botForm.notifyPrivateUserIds.map(Number).filter(n => n > 0),
        acTargetGroupId: Number(botForm.acTargetGroupId) || 0,
        autoReconnect: true,
      })
    });
    ElMessage.success(res.message || '设置已保存');
    await loadBotStatus();
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    bot.saving = false;
  }
}

function addAllowedGroup(groupId) {
  if (!botForm.allowedGroupIds.includes(groupId)) {
    botForm.allowedGroupIds.push(groupId);
    ElMessage.success('已添加到白名单（记得点击下方保存设置）');
  }
}

function openTestDialog() {
  testDialog.isGroup = true;
  testDialog.targetId = bot.groups.length > 0 ? bot.groups[0].groupId : null;
  testDialog.message = '这是一条来自 Qridge Web 面板的测试消息';
  testDialog.visible = true;
}

async function submitTestMessage() {
  if (!testDialog.targetId || Number(testDialog.targetId) <= 0) {
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
        targetId: Number(testDialog.targetId),
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

export function useBot() {
  return {
    bot,
    botForm,
    testDialog,
    loadBotStatus,
    reconnectBot,
    saveBotSettings,
    addAllowedGroup,
    openTestDialog,
    submitTestMessage,
  };
}
