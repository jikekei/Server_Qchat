import { page } from '../api.js';
import { useLocalAdmin } from '../modules/useLocalAdmin.js';

export const LoggingView = {
  name: 'LoggingView',
  template: `
        <div v-if="page === 'logging'">
          <div class="page-header">
            <div class="page-header-meta">
              <h3 class="page-title">日志级别</h3>
            </div>
            <div class="page-actions">
              <el-button :loading="logSettings.loading" @click="loadLogSettings">重新读取</el-button>
              <el-button type="primary" :loading="logSettings.saving" @click="saveLogSettings">保存配置</el-button>
            </div>
          </div>

          <el-card shadow="never">
            <template #header>
              <div class="card-head">
                <div class="card-head-title-group">
                  <span :title="logSettings.configPath || 'logging-level.json'">日志配置</span>
                </div>
                <div style="display:flex;align-items:center;gap:10px">
                  <span class="muted" style="font-size:12.5px;font-weight:600">全局默认级别：</span>
                  <el-select v-model="logSettings.defaultLevel" style="width:170px">
                    <el-option v-for="l in logSettings.levels" :key="l" :label="l" :value="l"></el-option>
                  </el-select>
                  <el-button type="primary" plain @click="addLogCategory">+ 添加分类覆盖</el-button>
                </div>
              </div>
            </template>

            <el-table :data="logSettings.categories" border stripe empty-text="当前未配置分类覆盖，所有模块均使用全局默认级别">
              <el-table-column label="日志分类名（Category 前缀或完整命名空间）" min-width="300">
                <template #default="{ row }">
                  <el-select v-model="row.category" filterable allow-create default-first-option
                             placeholder="选择推荐分类或直接输入自定义命名空间" style="width:100%">
                    <el-option v-for="c in logSettings.suggested" :key="c" :label="c" :value="c"></el-option>
                  </el-select>
                </template>
              </el-table-column>
              <el-table-column label="指定输出级别" width="210">
                <template #default="{ row }">
                  <el-select v-model="row.level" style="width:100%">
                    <el-option v-for="l in logSettings.levels" :key="l" :label="l" :value="l"></el-option>
                  </el-select>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="100" align="center">
                <template #default="{ $index }">
                  <el-button link type="danger" @click="logSettings.categories.splice($index, 1)">移除</el-button>
                </template>
              </el-table-column>
            </el-table>

            <div style="margin-top:16px;padding:12px 14px;background:var(--c-bg-subtle);border:1px solid var(--c-border-subtle);border-radius:var(--r-md);font-size:12.5px;color:var(--c-text-secondary);line-height:1.65">
              <div style="font-weight:600;color:var(--c-text);margin-bottom:4px">日志级别由宽到严说明：</div>
              <div>
                <code>Trace</code> → <code>Debug</code> → <code>Information</code> → <code>Warning</code> → <code>Error</code> → <code>Critical</code> → <code>None</code>
                （级别越靠后，输出的日志越精简）。
              </div>
              <div style="margin-top:4px">
                排查特定模块故障时，可将 <code>Server.Qcat.LocalAdmin</code> 或 <code>Server.Qcat.Socket</code> 单独设为 <code>Debug</code>，避免全局日志刷屏。
              </div>
            </div>
          </el-card>
        </div>
`,
  setup() {
    const { logSettings, loadLogSettings, saveLogSettings, addLogCategory } = useLocalAdmin();
    return {
      page,
      logSettings,
      loadLogSettings,
      saveLogSettings,
      addLogCategory,
    };
  }
};
