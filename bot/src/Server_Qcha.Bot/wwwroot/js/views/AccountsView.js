import { useAccounts } from '../modules/useAccounts.js';
import { permissionName, fmtTime } from '../api.js';

export const AccountsView = {
  name: 'AccountsView',
  template: `
        <div>
          <div class="toolbar">
            <el-button type="primary" @click="openAccountDialog(null)">新建账号</el-button>
            <el-button :loading="loadingAccounts" @click="loadAccounts">刷新</el-button>
          </div>
          <el-table :data="accounts" border stripe>
            <el-table-column prop="id" label="ID" width="60"></el-table-column>
            <el-table-column prop="username" label="用户名" width="140"></el-table-column>
            <el-table-column prop="displayName" label="显示名" width="140"></el-table-column>
            <el-table-column label="权限">
              <template #default="{ row }">
                <template v-if="row.permissionKeys.length">
                  <el-tag v-for="k in row.permissionKeys" :key="k" size="small" style="margin:2px 4px 2px 0">
                    {{ permissionName(k) }}
                  </el-tag>
                </template>
                <span v-else class="muted">无</span>
              </template>
            </el-table-column>
            <el-table-column label="状态" width="120">
              <template #default="{ row }">
                <el-tag size="small" :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '启用' : '禁用' }}</el-tag>
                <el-tag v-if="row.isBuiltIn" size="small" type="warning" style="margin-left:4px">内置</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="最后登录" width="170">
              <template #default="{ row }">{{ fmtTime(row.lastLoginAt) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="230">
              <template #default="{ row }">
                <el-button link size="small" @click="openAccountDialog(row)">编辑</el-button>
                <el-button link size="small" @click="resetAccountPassword(row)">重置密码</el-button>
                <el-button link size="small" type="danger" :disabled="row.isBuiltIn" @click="deleteAccount(row)">删除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </div>
`,
  setup() {
    const accounts = useAccounts();
    return {
      ...accounts,
      permissionName,
      fmtTime,
    };
  }
};
