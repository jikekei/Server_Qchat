import { useDatabase } from '../modules/useDatabase.js';
import { fmtTime } from '../api.js';

export const DatabaseView = {
  name: 'DatabaseView',
  template: `
        <div>
          <el-tabs v-model="db.activeTab" @tab-change="onDbTabChange">
            <!-- 标签页 1：玩家数据 (PlayerData) -->
            <el-tab-pane label="玩家数据 (PlayerData)" name="players">
              <div class="toolbar">
                <el-input v-model="playerQuery.search" placeholder="搜索玩家 ID / 昵称 / QQ" style="width:240px" clearable @keyup.enter="loadPlayers(1)" @clear="loadPlayers(1)"></el-input>
                <el-button type="primary" :loading="playerState.loading" @click="loadPlayers(1)">查询</el-button>
                <el-button type="success" @click="openPlayerDialog(null)">新建玩家记录</el-button>
                <el-button type="warning" plain @click="openRankingDialog">排行榜</el-button>
                <el-button :loading="playerState.loading" @click="loadPlayers()">刷新</el-button>
                <span class="muted">共 {{ playerState.total }} 条玩家记录</span>
              </div>

              <el-table :data="playerState.items" border stripe v-loading="playerState.loading">
                <el-table-column prop="id" label="目标 ID (SteamID)" min-width="160" show-overflow-tooltip></el-table-column>
                <el-table-column prop="playerName" label="昵称" min-width="140" show-overflow-tooltip></el-table-column>
                <el-table-column label="绑定 QQ" width="130">
                  <template #default="{ row }">
                    <span v-if="row.qqId && row.qqId > 0">{{ row.qqId }}</span>
                    <span v-else class="muted">未绑定</span>
                  </template>
                </el-table-column>
                <el-table-column label="身份" width="110">
                  <template #default="{ row }">
                    <el-tag size="small" :type="row.isAdmin ? 'danger' : 'info'">{{ row.isAdmin ? (row.adminNote || '管理员') : '玩家' }}</el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="游玩时长" width="130" sortable>
                  <template #default="{ row }">
                    {{ formatPlayTime(row.playTimeSeconds) }}
                  </template>
                </el-table-column>
                <el-table-column prop="playersKilled" label="杀敌" width="80" align="center"></el-table-column>
                <el-table-column prop="scpsKilled" label="杀SCP" width="80" align="center"></el-table-column>
                <el-table-column prop="deaths" label="死亡" width="80" align="center"></el-table-column>
                <el-table-column prop="kd" label="K/D" width="80" align="center">
                  <template #default="{ row }">
                    <b :style="{ color: row.kd >= 1.5 ? 'var(--el-color-success)' : (row.kd < 0.8 ? 'var(--el-color-danger)' : '') }">{{ row.kd }}</b>
                  </template>
                </el-table-column>
                <el-table-column label="操作" width="150" fixed="right">
                  <template #default="{ row }">
                    <el-button link size="small" @click="openPlayerDialog(row)">编辑</el-button>
                    <el-button link size="small" type="danger" @click="deletePlayer(row)">删除</el-button>
                  </template>
                </el-table-column>
              </el-table>

              <div style="margin-top:16px;display:flex;justify-content:flex-end">
                <el-pagination
                  background
                  layout="total, sizes, prev, pager, next, jumper"
                  :total="playerState.total"
                  :page-size="playerQuery.pageSize"
                  :current-page="playerQuery.page"
                  :page-sizes="[10, 20, 50, 100]"
                  @size-change="onPlayerSizeChange"
                  @current-change="onPlayerPageChange">
                </el-pagination>
              </div>
            </el-tab-pane>

            <!-- 标签页 2：封禁管理 (BanPlayerData) -->
            <el-tab-pane label="封禁管理 (BanPlayerData)" name="bans">
              <div class="toolbar">
                <el-input v-model="banQuery.search" placeholder="搜索 ID / IP / 管理员 / 原因" style="width:260px" clearable @keyup.enter="loadBans(1)" @clear="loadBans(1)"></el-input>
                <el-checkbox v-model="banQuery.activeOnly" @change="loadBans(1)">仅有效封禁</el-checkbox>
                <el-button type="primary" :loading="banState.loading" @click="loadBans(1)">查询</el-button>
                <el-button type="danger" @click="openBanDialog(null)">新增封禁</el-button>
                <el-button :loading="banState.loading" @click="loadBans()">刷新</el-button>
                <span class="muted">共 {{ banState.total }} 条封禁记录</span>
              </div>

              <el-table :data="banState.items" border stripe v-loading="banState.loading">
                <el-table-column prop="id" label="目标 ID (SteamID)" min-width="160" show-overflow-tooltip></el-table-column>
                <el-table-column prop="playerIP" label="玩家 IP" width="130"></el-table-column>
                <el-table-column label="解封时间" width="170">
                  <template #default="{ row }">
                    <span v-if="row.isPermanent" style="font-weight:600;color:var(--el-color-danger)">永久封禁</span>
                    <span v-else>{{ fmtTime(row.unbanTime) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="状态" width="90" align="center">
                  <template #default="{ row }">
                    <el-tag size="small" :type="row.isActive ? 'danger' : 'info'">{{ row.isActive ? '封禁中' : '已解封' }}</el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="执行管理员" width="140" show-overflow-tooltip>
                  <template #default="{ row }">
                    {{ row.adminName || row.adminId || '-' }}
                  </template>
                </el-table-column>
                <el-table-column prop="reason" label="封禁原因" min-width="160" show-overflow-tooltip></el-table-column>
                <el-table-column label="操作" width="150" fixed="right">
                  <template #default="{ row }">
                    <el-button link size="small" @click="openBanDialog(row)">编辑</el-button>
                    <el-button link size="small" type="success" v-if="row.isActive" @click="deleteBan(row, 'unban')">解封</el-button>
                    <el-button link size="small" type="danger" v-else @click="deleteBan(row, 'delete')">删除记录</el-button>
                  </template>
                </el-table-column>
              </el-table>

              <div style="margin-top:16px;display:flex;justify-content:flex-end">
                <el-pagination
                  background
                  layout="total, sizes, prev, pager, next, jumper"
                  :total="banState.total"
                  :page-size="banQuery.pageSize"
                  :current-page="banQuery.page"
                  :page-sizes="[10, 20, 50, 100]"
                  @size-change="onBanSizeChange"
                  @current-change="onBanPageChange">
                </el-pagination>
              </div>
            </el-tab-pane>

            <!-- 标签页 3：数据库配置与维护 -->
            <el-tab-pane label="配置与维护" name="config">
              <el-row :gutter="16">
                <el-col :span="14">
                  <el-card class="server-card" style="margin-bottom:16px">
                    <template #header>
                      <div style="font-weight:600">MySQL 连接设置</div>
                    </template>
                    <el-form label-width="120px">
                      <el-form-item label="连接字符串">
                        <el-input v-model="dbForm.connectionString" type="textarea" :rows="3" placeholder="Server=127.0.0.1;Port=3306;Database=scpsl;Uid=root;Pwd=password;"></el-input>
                        <div class="muted">用于连接游戏插件所共享的 MySQL 数据库，支持热更新，修改后保存立即生效。</div>
                      </el-form-item>
                      <el-form-item>
                        <el-button type="primary" :loading="db.saving" @click="saveDbConfig">保存配置</el-button>
                        <el-button type="warning" plain :loading="db.testing" @click="testDbConnection">测试连接</el-button>
                        <el-button :loading="db.loadingStatus" @click="loadDbStatus">重新读取</el-button>
                      </el-form-item>
                    </el-form>
                    <el-alert v-if="db.testResult" :type="db.testResult.success ? 'success' : 'error'" :closable="false" show-icon style="margin-top:12px" :title="db.testResult.title">
                      <div>{{ db.testResult.detail }}</div>
                    </el-alert>
                  </el-card>

                  <el-card class="server-card">
                    <template #header>
                      <div style="font-weight:600">数据表维护</div>
                    </template>
                    <p class="muted" style="margin-top:0">
                      一键在 MySQL 数据库中初始化 <code>PlayerData</code> 和 <code>BanPlayerData</code> 数据表结构。<br />
                      使用 <code>CREATE TABLE IF NOT EXISTS</code>，若表已存在不会影响已有数据。
                    </p>
                    <el-button type="danger" plain :loading="db.initializing" @click="initDbTables">一键初始化数据表结构</el-button>
                  </el-card>
                </el-col>

                <el-col :span="10">
                  <el-card class="server-card">
                    <template #header>
                      <div style="font-weight:600">数据库概览统计</div>
                    </template>
                    <el-descriptions :column="1" border size="small">
                      <el-descriptions-item label="连接状态">
                        <el-tag size="small" :type="db.status.connected ? 'success' : 'danger'">{{ db.status.connected ? '正常连接' : '未连接 / 异常' }}</el-tag>
                      </el-descriptions-item>
                      <el-descriptions-item label="数据库版本">{{ db.status.summary && db.status.summary.mysqlVersion ? db.status.summary.mysqlVersion : '-' }}</el-descriptions-item>
                      <el-descriptions-item label="玩家总数">{{ db.status.summary ? db.status.summary.totalPlayers : '-' }}</el-descriptions-item>
                      <el-descriptions-item label="管理员数">{{ db.status.summary ? db.status.summary.adminPlayers : '-' }}</el-descriptions-item>
                      <el-descriptions-item label="封禁总数">{{ db.status.summary ? db.status.summary.totalBans : '-' }}</el-descriptions-item>
                      <el-descriptions-item label="有效封禁">{{ db.status.summary ? db.status.summary.activeBans : '-' }}</el-descriptions-item>
                    </el-descriptions>
                    <el-alert v-if="db.status.summary && db.status.summary.error" type="error" :closable="false" show-icon style="margin-top:12px" :title="'错误：' + db.status.summary.error"></el-alert>
                  </el-card>
                </el-col>
              </el-row>
            </el-tab-pane>
          </el-tabs>
        </div>
`,
  setup() {
    const database = useDatabase();
    return {
      ...database,
      fmtTime,
    };
  }
};
