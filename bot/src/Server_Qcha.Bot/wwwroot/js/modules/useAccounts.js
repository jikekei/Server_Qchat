import { ref, reactive } from '../deps.js';
import { api, meta, randomPassword } from '../api.js';
import { ElMessage, ElMessageBox } from '../deps.js';

const accounts = ref([]);
const loadingAccounts = ref(false);

const accountDialog = reactive({
  visible: false, loading: false, id: null, username: '', displayName: '',
  password: '', permissionKeys: [], isEnabled: true, isBuiltIn: false
});

async function loadAccounts() {
  loadingAccounts.value = true;
  try {
    accounts.value = await api('/accounts');
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    loadingAccounts.value = false;
  }
}

function openAccountDialog(row) {
  accountDialog.id = row ? row.id : null;
  accountDialog.username = row ? row.username : '';
  accountDialog.displayName = row ? row.displayName : '';
  accountDialog.password = row ? '' : randomPassword();
  accountDialog.permissionKeys = row ? row.permissionKeys.slice() : ['servers.view'];
  accountDialog.isEnabled = row ? row.isEnabled : true;
  accountDialog.isBuiltIn = row ? !!row.isBuiltIn : false;
  accountDialog.visible = true;
}

function applyPreset(preset) {
  accountDialog.permissionKeys = meta.permissions
    .filter(p => p.value !== 0 && (preset.value & p.value) === p.value)
    .map(p => p.key);
}

async function saveAccount() {
  if (!accountDialog.id && (!accountDialog.username || accountDialog.username.length < 3)) {
    ElMessage.warning('用户名至少 3 个字符');
    return;
  }
  if (!accountDialog.id && (!accountDialog.password || accountDialog.password.length < 8)) {
    ElMessage.warning('密码至少 8 位');
    return;
  }

  accountDialog.loading = true;
  try {
    if (accountDialog.id) {
      await api('/accounts/' + accountDialog.id, {
        method: 'PUT',
        body: JSON.stringify({
          displayName: accountDialog.displayName,
          permissionKeys: accountDialog.permissionKeys,
          isEnabled: accountDialog.isEnabled
        })
      });
      ElMessage.success('账号已更新');
    } else {
      await api('/accounts', {
        method: 'POST',
        body: JSON.stringify({
          username: accountDialog.username,
          password: accountDialog.password,
          displayName: accountDialog.displayName,
          permissionKeys: accountDialog.permissionKeys,
          isEnabled: accountDialog.isEnabled
        })
      });
      ElMessage.success('账号已创建');
    }
    accountDialog.visible = false;
    await loadAccounts();
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    accountDialog.loading = false;
  }
}

async function resetAccountPassword(row) {
  let generated = false;
  try {
    await ElMessageBox.confirm(
      '将随机生成新密码并强制该账号下线，确定继续吗？',
      '重置密码 · ' + row.username,
      { type: 'warning', confirmButtonText: '确定', cancelButtonText: '取消' }
    );
  } catch (e) {
    return;
  }

  try {
    const res = await api('/accounts/' + row.id + '/password', { method: 'POST', body: JSON.stringify({}) });
    generated = true;
    await ElMessageBox.alert(
      '新密码（请立即保存）：' + res.password,
      '重置成功',
      { confirmButtonText: '我已保存' }
    );
    await loadAccounts();
  } catch (e) {
    if (!generated) ElMessage.error(e.message);
  }
}

async function deleteAccount(row) {
  try {
    await ElMessageBox.confirm(
      '确定删除账号【' + row.username + '】吗？此操作不可恢复。',
      '删除账号',
      { type: 'error', confirmButtonText: '删除', cancelButtonText: '取消' }
    );
  } catch (e) {
    return;
  }

  try {
    await api('/accounts/' + row.id, { method: 'DELETE' });
    ElMessage.success('账号已删除');
    await loadAccounts();
  } catch (e) {
    ElMessage.error(e.message);
  }
}

// ---- 修改自己密码 ----
const passwordDialog = reactive({ visible: false, loading: false, current: '', next: '' });

async function submitPassword() {
  if (!passwordDialog.next || passwordDialog.next.length < 8) {
    ElMessage.warning('新密码至少 8 位');
    return;
  }
  passwordDialog.loading = true;
  try {
    await api('/auth/password', {
      method: 'POST',
      body: JSON.stringify({ currentPassword: passwordDialog.current, newPassword: passwordDialog.next })
    });
    ElMessage.success('密码已修改');
    passwordDialog.visible = false;
    passwordDialog.current = '';
    passwordDialog.next = '';
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    passwordDialog.loading = false;
  }
}

export function useAccounts() {
  return {
    accounts,
    loadingAccounts,
    loadAccounts,
    accountDialog,
    openAccountDialog,
    applyPreset,
    saveAccount,
    resetAccountPassword,
    deleteAccount,
    passwordDialog,
    submitPassword,
  };
}
