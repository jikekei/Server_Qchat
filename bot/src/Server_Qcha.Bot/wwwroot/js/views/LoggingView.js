import { page } from '../api.js';
import { useLocalAdmin } from '../modules/useLocalAdmin.js';

export const LoggingView = {
  name: 'LoggingView',
  template: `
        <div v-if="page === 'logging'">
          <el-alert type="info" :closable="false" show-icon
                    title="日志级别修改后约 1 秒内即时生效，无需重启机器人" style="margin-bottom:12px">
            <div>写入 <code>{{ logSettings.configPath || 'logging-level.json' }}</code>，由配置热重载生效。</div>
            <div style="margin-top:4px">{{ logSettings.note }}</div>
          </el-alert>

          <div class="toolbar">
            <span style="margin-right:6px">默认级别</span>
            <el-select v-model="logSettings.defaultLevel" style="width:180px">
              <el-option v-for="l in logSettings.levels" :key="l" :label="l" :value="l"></el-option>
            </el-select>
            <el-button :loading="logSettings.loading" @click="loadLogSettings">重新读取</el-button>
            <el-button type="primary" :loading="logSettings.saving" @click="saveLogSettings">保存</el-button>
          </div>

          <h4 style="margin:18px 0 8px">分类覆盖（不设置则继承默认级别）</h4>
          <el-table :data="logSettings.categories" border size="small" style="max-width:760px">
            <el-table-column label="分类名（日志 Category）">
              <template #default="{ row }">
                <el-select v-model="row.category" filterable allow-create default-first-option
                           placeholder="选择或直接输入分类名" style="width:100%">
                  <el-option v-for="c in logSettings.suggested" :key="c" :label="c" :value="c"></el-option>
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="级别" width="190">
              <template #default="{ row }">
                <el-select v-model="row.level">
                  <el-option v-for="l in logSettings.levels" :key="l" :label="l" :value="l"></el-option>
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="90">
              <template #default="{ $index }">
                <el-button link type="danger" @click="logSettings.categories.splice($index, 1)">删除</el-button>
              </template>
            </el-table-column>
          </el-table>

          <div style="margin-top:10px">
            <el-button @click="addLogCategory">添加分类</el-button>
          </div>

          <p class="muted" style="margin-top:14px">
            级别由宽到严：Trace → Debug → Information → Warning → Error → Critical → None —— 设得越严，输出的日志越少。
            排查问题时可以把 <code>Server.Qcat.LocalAdmin</code> 或 <code>Server.Qcat.Socket</code> 单独设为 Debug，
            而不必让整个应用变啰嗦。
          </p>
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
