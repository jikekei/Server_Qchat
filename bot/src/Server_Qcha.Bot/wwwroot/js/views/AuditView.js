import { useAudit } from '../modules/useAudit.js';
import { fmtTime } from '../api.js';

export const AuditView = {
  name: 'AuditView',
  template: `
        <div>
          <div class="toolbar">
            <el-button :loading="loadingAudit" @click="loadAudit">刷新</el-button>
            <span class="muted">最近 {{ audit.length }} 条</span>
          </div>
          <el-table :data="audit" border stripe>
            <el-table-column label="时间" width="170">
              <template #default="{ row }">{{ fmtTime(row.createdAt) }}</template>
            </el-table-column>
            <el-table-column prop="username" label="操作者" width="130"></el-table-column>
            <el-table-column prop="action" label="操作" width="190"></el-table-column>
            <el-table-column prop="target" label="对象" width="220"></el-table-column>
            <el-table-column prop="detail" label="详情" show-overflow-tooltip></el-table-column>
            <el-table-column label="结果" width="80">
              <template #default="{ row }">
                <el-tag size="small" :type="row.success ? 'success' : 'danger'">{{ row.success ? '成功' : '失败' }}</el-tag>
              </template>
            </el-table-column>
          </el-table>
        </div>
`,
  setup() {
    const audit = useAudit();
    return {
      ...audit,
      fmtTime,
    };
  }
};
