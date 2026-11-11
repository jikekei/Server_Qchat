import { ref, computed } from '../deps.js';
import { useAudit } from '../modules/useAudit.js';
import { fmtTime } from '../api.js';

export const AuditView = {
  name: 'AuditView',
  template: `
        <div>
          <div class="page-header">
            <div class="page-header-meta">
              <h3 class="page-title">操作审计日志</h3>
            </div>
            <div class="page-actions">
              <el-input v-model="keyword" placeholder="搜索操作者、动作、对象或详情..." clearable style="width:260px" />
              <el-radio-group v-model="statusFilter" size="default">
                <el-radio-button value="all">全部 ({{ audit.length }})</el-radio-button>
                <el-radio-button value="ok">成功</el-radio-button>
                <el-radio-button value="fail">失败</el-radio-button>
              </el-radio-group>
              <el-button :loading="loadingAudit" @click="loadAudit">刷新日志</el-button>
            </div>
          </div>

          <el-card shadow="never">
            <el-table :data="filteredAudit" border stripe v-loading="loadingAudit" empty-text="暂无符合筛选条件的操作审计记录">
              <el-table-column label="执行时间" width="175">
                <template #default="{ row }">
                  <span class="mono" style="font-size:12px">{{ fmtTime(row.createdAt) }}</span>
                </template>
              </el-table-column>
              <el-table-column prop="username" label="操作者" width="135">
                <template #default="{ row }">
                  <b>{{ row.username || '-' }}</b>
                </template>
              </el-table-column>
              <el-table-column prop="action" label="动作标识 (Action)" width="200">
                <template #default="{ row }">
                  <code style="font-size:12px">{{ row.action }}</code>
                </template>
              </el-table-column>
              <el-table-column prop="target" label="目标对象" width="220" show-overflow-tooltip>
                <template #default="{ row }">
                  <span class="mono" style="font-size:12px">{{ row.target || '-' }}</span>
                </template>
              </el-table-column>
              <el-table-column prop="detail" label="变更详情" min-width="240" show-overflow-tooltip>
                <template #default="{ row }">
                  <span style="color:var(--c-text-secondary)">{{ row.detail || '-' }}</span>
                </template>
              </el-table-column>
              <el-table-column label="执行结果" width="95" align="center" fixed="right">
                <template #default="{ row }">
                  <el-tag size="small" :type="row.success ? 'success' : 'danger'">{{ row.success ? '成功' : '失败' }}</el-tag>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </div>
`,
  setup() {
    const auditModule = useAudit();
    const keyword = ref('');
    const statusFilter = ref('all');

    const filteredAudit = computed(() => {
      const q = keyword.value.trim().toLowerCase();
      return (auditModule.audit.value || []).filter(item => {
        if (statusFilter.value === 'ok' && !item.success) return false;
        if (statusFilter.value === 'fail' && item.success) return false;
        if (!q) return true;
        return `${item.username || ''} ${item.action || ''} ${item.target || ''} ${item.detail || ''}`
          .toLowerCase()
          .includes(q);
      });
    });

    return {
      ...auditModule,
      keyword,
      statusFilter,
      filteredAudit,
      fmtTime,
    };
  }
};
