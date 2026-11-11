import { useAccounts } from '../modules/useAccounts.js';
import { permissionName, fmtTime } from '../api.js';

export const AccountsView = {
  name: 'AccountsView',
  template: `
        <div>
          <div class="page-header">
            <div class="page-header-meta">
              <h3 class="page-title">面板账号与 RBAC 权限管理</h3>
              <span class="page-subtitle">分配 Web 运维面板的登录账号、角色预设与细粒度操作权限</span>
            </div>
            <div class="page-actions">
              <el-button type="primary" @click="openAccountDialog(null)">+ 新建面板账号</el-button>
              <el-button :loading="loadingAccounts" @click="loadAccounts">刷新列表</el-button>
            </div>
          </div>

          <el-card shadow="never">
            <el-table :data="accounts" border stripe v-loading="loadingAccounts">
              <el-table-column prop="id" label="ID" width="68" align="center">
                <template #default="{ row }">
                  <span class="mono muted">#{{ row.id }}</span>
                </template>
              </el-table-column>
              <el-table-column prop="username" label="登录用户名" width="150">
                <template #default="{ row }">
                  <b class="mono">{{ row.username }}</b>
                </template>
              </el-table-column>
              <el-table-column prop="displayName" label="显示名称" width="150">
                <template #default="{ row }">
                  <span>{{ row.displayName || row.username }}</span>
                </template>
              </el-table-column>
              <el-table-column label="已授权模块与操作权限" min-width="260">
                <template #default="{ row }">
                  <template v-if="row.permissionKeys.length">
                    <el-tag v-for="k in row.permissionKeys" :key="k" size="small" effect="plain" style="margin:2px 5px 2px 0">
                      {{ permissionName(k) }}
                    </el-tag>
                  </template>
                  <span v-else class="muted">无任何权限</span>
                </template>
              </el-table-column>
              <el-table-column label="账号状态" width="135">
                <template #default="{ row }">
                  <el-tag size="small" :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '已启用' : '已禁用' }}</el-tag>
                  <el-tag v-if="row.isBuiltIn" size="small" type="warning" style="margin-left:4px">系统内置</el-tag>
                </template>
              </el-table-column>
              <el-table-column label="最后登录时间" width="175">
                <template #default="{ row }">
                  <span class="mono" style="font-size:12px">{{ fmtTime(row.lastLoginAt) }}</span>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="220" fixed="right">
                <template #default="{ row }">
                  <el-button link size="small" type="primary" @click="openAccountDialog(row)">编辑权限</el-button>
                  <el-button link size="small" type="warning" @click="resetAccountPassword(row)">重置密码</el-button>
                  <el-button link size="small" type="danger" :disabled="row.isBuiltIn" @click="deleteAccount(row)">删除</el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
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
