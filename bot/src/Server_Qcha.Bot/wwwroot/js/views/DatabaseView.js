import { useDatabase } from '../modules/useDatabase.js';
import { fmtTime } from '../api.js';

export const DatabaseView = {
  name: 'DatabaseView',
  template: `
        <div>
          <div class="page-header">
            <div class="page-header-info">
              <h2>MySQL 数据库管理</h2>
              <p>管理与游戏插件共享的 PlayerData 玩家战绩库、BanPlayerData 封禁记录及表结构维护</p>
            </div>
            <div class="page-header-actions">
              <el-button size="small" type="warning" plain @click="openRankingDialog">🏆 玩家排行榜</el-button>
              <el-button size="small" type="success" plain @click="openPlayerDialog(null)">+ 新建玩家记录</el-button>
              <el-button size="small" type="danger" plain @click="openBanDialog(null)">+ 新增封禁</el-button>
            </div>
          </div>

          <el-card shadow="never">
            <el-tabs v-model="db.activeTab" @tab-change="onDbTabChange">
              <!-- 标签页 1：玩家数据 (PlayerData) -->
              <el-tab-pane label="玩家数据 (PlayerData)" name="players">
                <div class="toolbar">
                  <el-input v-model="playerQuery.search" placeholder="搜索玩家 SteamID / 昵称 / 绑定 QQ" style="width:280px" clearable @keyup.enter="loadPlayers(1)" @clear="loadPlayers(1)"></el-input>
                  <el-button type="primary" :loading="playerState.loading" @click="loadPlayers(1)">查询</el-button>
                  <el-button :loading="playerState.loading" @click="loadPlayers()">刷新</el-button>
                  <span class="muted" style="margin-left:auto">共 <b>{{ playerState.total }}</b> 条玩家记录</span>
                </div>

                <el-table :data="playerState.items" border stripe v-loading="playerState.loading">
                  <el-table-column prop="id" label="目标 ID (SteamID)" min-width="175" show-overflow-tooltip>
                    <template #default="{ row }">
                      <code>{{ row.id }}</code>
                    </template>
                  </el-table-column>
                  <el-table-column prop="playerName" label="游戏昵称" min-width="140" show-overflow-tooltip>
                    <template #default="{ row }">
                      <b>{{ row.playerName }}</b>
                    </template>
                  </el-table-column>
                  <el-table-column label="绑定 QQ" width="135">
                    <template #default="{ row }">
                      <span v-if="row.qqId && row.qqId > 0" class="mono">{{ row.qqId }}</span>
                      <span v-else class="muted">未绑定</span>
                    </template>
                  </el-table-column>
                  <el-table-column label="身份标识" width="115">
                    <template #default="{ row }">
                      <el-tag size="small" :type="row.isAdmin ? 'danger' : 'info'">{{ row.isAdmin ? (row.adminNote || '管理员') : '普通玩家' }}</el-tag>
                    </template>
                  </el-table-column>
                  <el-table-column label="游玩时长" width="135" sortable>
                    <template #default="{ row }">
                      <span class="mono">{{ formatPlayTime(row.playTimeSeconds) }}</span>
                    </template>
                  </el-table-column>
                  <el-table-column prop="playersKilled" label="击杀玩家" width="90" align="center"></el-table-column>
                  <el-table-column prop="scpsKilled" label="击杀 SCP" width="90" align="center"></el-table-column>
                  <el-table-column prop="deaths" label="死亡" width="80" align="center"></el-table-column>
                  <el-table-column prop="kd" label="K/D" width="85" align="center">
                    <template #default="{ row }">
                      <b class="mono" :style="{ color: row.kd >= 1.5 ? 'var(--qb-success)' : (row.kd < 0.8 ? 'var(--qb-danger)' : '') }">{{ row.kd }}</b>
                    </template>
                  </el-table-column>
                  <el-table-column label="操作" width="140" fixed="right">
                    <template #default="{ row }">
                      <el-button link size="small" type="primary" @click="openPlayerDialog(row)">编辑</el-button>
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
                  <el-input v-model="banQuery.search" placeholder="搜索 ID / IP / 管理员 / 封禁原因" style="width:280px" clearable @keyup.enter="loadBans(1)" @clear="loadBans(1)"></el-input>
                  <el-checkbox v-model="banQuery.activeOnly" @change="loadBans(1)">仅显示生效中封禁</el-checkbox>
                  <el-button type="primary" :loading="banState.loading" @click="loadBans(1)">查询</el-button>
                  <el-button :loading="banState.loading" @click="loadBans()">刷新</el-button>
                  <span class="muted" style="margin-left:auto">共 <b>{{ banState.total }}</b> 条封禁记录</span>
                </div>

                <el-table :data="banState.items" border stripe v-loading="banState.loading">
                  <el-table-column prop="id" label="目标 ID (SteamID)" min-width="175" show-overflow-tooltip>
                    <template #default="{ row }">
                      <code>{{ row.id }}</code>
                    </template>
                  </el-table-column>
                  <el-table-column prop="playerIP" label="玩家 IP" width="135">
                    <template #default="{ row }">
                      <span class="mono">{{ row.playerIP || '-' }}</span>
                    </template>
                  </el-table-column>
                  <el-table-column label="解封时间" width="175">
                    <template #default="{ row }">
                      <span v-if="row.isPermanent" style="font-weight:700;color:var(--qb-danger)">永久封禁</span>
                      <span v-else class="mono">{{ fmtTime(row.unbanTime) }}</span>
                    </template>
                  </el-table-column>
                  <el-table-column label="状态" width="95" align="center">
                    <template #default="{ row }">
                      <el-tag size="small" :type="row.isActive ? 'danger' : 'info'" :effect="row.isActive ? 'dark' : 'plain'">
                        {{ row.isActive ? '封禁中' : '已解封' }}
                      </el-tag>
                    </template>
                  </el-table-column>
                  <el-table-column label="执行管理员" width="145" show-overflow-tooltip>
                    <template #default="{ row }">
                      {{ row.adminName || row.adminId || '-' }}
                    </template>
                  </el-table-column>
                  <el-table-column prop="reason" label="封禁原因" min-width="170" show-overflow-tooltip></el-table-column>
                  <el-table-column label="操作" width="150" fixed="right">
                    <template #default="{ row }">
                      <el-button link size="small" type="primary" @click="openBanDialog(row)">编辑</el-button>
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
              <el-tab-pane label="连接配置与表结构维护" name="config">
                <div class="metric-tiles" style="margin-bottom:18px">
                  <div class="metric-tile">
                    <div class="metric-tile-label">数据库连接状态</div>
                    <div class="metric-tile-value">
                      <el-tag size="small" :type="db.status.connected ? 'success' : 'danger'" effect="dark">
                        {{ db.status.connected ? '正常连接' : '未连接 / 异常' }}
                      </el-tag>
                    </div>
                    <div class="metric-tile-sub">MySQL 版本：{{ (db.status.summary && db.status.summary.mysqlVersion) || '-' }}</div>
                  </div>
                  <div class="metric-tile">
                    <div class="metric-tile-label">已记录玩家总数</div>
                    <div class="metric-tile-value">{{ db.status.summary ? db.status.summary.totalPlayers : '-' }}</div>
                    <div class="metric-tile-sub">其中标记管理员：{{ db.status.summary ? db.status.summary.adminPlayers : '-' }} 人</div>
                  </div>
                  <div class="metric-tile">
                    <div class="metric-tile-label">封禁记录统计</div>
                    <div class="metric-tile-value">{{ db.status.summary ? db.status.summary.activeBans : '-' }} <span style="font-size:12px;font-weight:400;color:var(--qb-text-3)">生效中</span></div>
                    <div class="metric-tile-sub">历史累计封禁：{{ db.status.summary ? db.status.summary.totalBans : '-' }} 条</div>
                  </div>
                </div>

                <el-alert v-if="db.status.summary && db.status.summary.error" type="error" :closable="false" show-icon style="margin-bottom:16px" :title="'数据库异常：' + db.status.summary.error"></el-alert>

                <el-row :gutter="16">
                  <el-col :xs="24" :md="14">
                    <el-card shadow="never" style="margin-bottom:16px;background:var(--qb-surface-2)">
                      <template #header>
                        <div style="font-weight:600">MySQL 连接字符串配置</div>
                      </template>
                      <el-form label-width="100px">
                        <el-form-item label="连接字符串">
                          <el-input v-model="dbForm.connectionString" type="textarea" :rows="3" class="mono" placeholder="Server=127.0.0.1;Port=3306;Database=scpsl;Uid=root;Pwd=password;"></el-input>
                          <div class="muted" style="margin-top:4px">用于连接游戏插件所共享的 MySQL 数据库，支持热更新，保存后立即生效。</div>
                        </el-form-item>
                        <el-form-item>
                          <el-button type="primary" :loading="db.saving" @click="saveDbConfig">保存连接配置</el-button>
                          <el-button type="warning" plain :loading="db.testing" @click="testDbConnection">测试连接</el-button>
                          <el-button :loading="db.loadingStatus" @click="loadDbStatus">重新读取</el-button>
                        </el-form-item>
                      </el-form>
                      <el-alert v-if="db.testResult" :type="db.testResult.success ? 'success' : 'error'" :closable="false" show-icon style="margin-top:12px" :title="db.testResult.title">
                        <div>{{ db.testResult.detail }}</div>
                      </el-alert>
                    </el-card>
                  </el-col>

                  <el-col :xs="24" :md="10">
                    <el-card shadow="never" style="background:var(--qb-surface-2)">
                      <template #header>
                        <div style="font-weight:600">数据表结构自动初始化</div>
                      </template>
                      <p class="muted" style="margin-top:0;line-height:1.6">
                        一键在当前 MySQL 数据库中创建 <code>PlayerData</code> 与 <code>BanPlayerData</code> 标准数据表结构。<br />
                        底层采用安全的 <code>CREATE TABLE IF NOT EXISTS</code> 语句，若表已存在不会覆盖或破坏既有玩家数据。
                      </p>
                      <el-button type="danger" plain :loading="db.initializing" @click="initDbTables">
                        🛠️ 一键初始化数据表结构
                      </el-button>
                    </el-card>
                  </el-col>
                </el-row>
              </el-tab-pane>
            </el-tabs>
          </el-card>
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
