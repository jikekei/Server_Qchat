import { useBot } from '../modules/useBot.js';
import { fmtTime } from '../api.js';

export const BotView = {
  name: 'BotView',
  template: `
        <div>
          <el-alert type="info" :closable="false" show-icon
                    title="QQ 机器人运行与联动管理" style="margin-bottom:12px">
            <div>连接 OneBot 11（推荐 NapCatQQ）正向 WebSocket，配置即时保存到 <code>{{ bot.configPath || 'bot-settings.json' }}</code> 并即刻热重载。</div>
          </el-alert>

          <!-- 状态与控制栏 -->
          <el-card class="server-card" style="margin-bottom:16px">
            <div class="server-head">
              <div style="display:flex;align-items:center;gap:10px">
                <span style="font-weight:600;font-size:16px">连接状态</span>
                <el-tag :type="bot.status.isConnected ? 'success' : (bot.status.nextReconnectInSeconds > 0 ? 'warning' : 'danger')" effect="dark">
                  {{ bot.status.isConnected ? '已连接' : (bot.status.nextReconnectInSeconds > 0 ? ('重连中 (' + bot.status.nextReconnectInSeconds + 's)') : '未连接') }}
                </el-tag>
              </div>
              <div style="display:flex;gap:8px">
                <el-button :loading="bot.loading" @click="loadBotStatus">刷新</el-button>
                <el-button type="primary" :loading="bot.reconnecting" @click="reconnectBot">重新连接</el-button>
                <el-button type="success" :disabled="!bot.status.isConnected" @click="openTestDialog">发送测试消息</el-button>
              </div>
            </div>

            <el-descriptions :column="3" border size="small" style="margin-top:12px">
              <el-descriptions-item label="登录 QQ">
                <template v-if="bot.status.connectedUserId">
                  <b>{{ bot.status.connectedNickname || 'QQ 用户' }}</b>
                  <span class="muted"> ({{ bot.status.connectedUserId }})</span>
                </template>
                <span v-else class="muted">（未获取）</span>
              </el-descriptions-item>
              <el-descriptions-item label="WS 目标">{{ bot.status.wsBaseUri || '-' }}</el-descriptions-item>
              <el-descriptions-item label="已加入群聊">{{ bot.groups.length }} 个群</el-descriptions-item>
              <el-descriptions-item label="上次连接">{{ fmtTime(bot.status.lastConnectedAt) }}</el-descriptions-item>
              <el-descriptions-item label="上次断开">{{ fmtTime(bot.status.lastDisconnectedAt) }}</el-descriptions-item>
              <el-descriptions-item label="累计重试">{{ bot.status.currentAttempt > 0 ? (bot.status.currentAttempt + ' 次') : '无' }}</el-descriptions-item>
            </el-descriptions>

            <el-alert v-if="bot.status.lastError" type="error" :closable="false" show-icon
                      :title="'最近错误：' + bot.status.lastError" style="margin-top:10px"></el-alert>
          </el-card>

          <!-- 设置表单 -->
          <el-card class="server-card" style="margin-bottom:16px">
            <template #header>
              <div style="font-weight:600">机器人设置</div>
            </template>

            <el-form label-width="150px" style="max-width:820px">
              <h4 style="margin:0 0 14px;color:var(--qb-accent)">一、OneBot 11 (NapCatQQ) 连接</h4>
              <el-form-item label="正向 WS 地址">
                <el-input v-model="botForm.wsBaseUri" placeholder="ws://127.0.0.1:6700"></el-input>
                <div class="muted">NapCatQQ 的正向 WebSocket 接口地址</div>
              </el-form-item>
              <el-form-item label="基础重连间隔">
                <el-input-number v-model="botForm.reconnectDelaySeconds" :min="1" :max="300" style="width:160px"></el-input-number>
                <span class="muted" style="margin-left:8px">秒（连接断开后基础等待时长）</span>
              </el-form-item>
              <el-form-item label="最大重连间隔">
                <el-input-number v-model="botForm.maxReconnectDelaySeconds" :min="1" :max="3600" style="width:160px"></el-input-number>
                <span class="muted" style="margin-left:8px">秒（连续重试退避上限）</span>
              </el-form-item>

              <el-divider></el-divider>

              <h4 style="margin:0 0 14px;color:var(--qb-accent)">二、群组白名单与推送通知</h4>
              <el-form-item label="群消息白名单">
                <el-select v-model="botForm.allowedGroupIds" multiple filterable allow-create default-first-option
                           placeholder="从已加入群组选择或直接输入群号" style="width:100%">
                  <el-option v-for="g in bot.groups" :key="g.groupId" :label="g.groupName + ' (' + g.groupId + ')'" :value="g.groupId"></el-option>
                </el-select>
                <div class="muted">仅响应白名单内群聊的指令。留空则监听机器人所在的所有群聊。</div>
              </el-form-item>

              <el-form-item label=".ac 游戏推送群">
                <el-select v-model="botForm.acTargetGroupId" filterable allow-create default-first-option
                           placeholder="选择接收游戏内 .ac 推送的群" style="width:100%">
                  <el-option :value="0" label="未设置（不单独推送）"></el-option>
                  <el-option v-for="g in bot.groups" :key="g.groupId" :label="g.groupName + ' (' + g.groupId + ')'" :value="g.groupId"></el-option>
                </el-select>
                <div class="muted">玩家在游戏内输入 <code>.ac 消息</code> 时接收实时推送的群。若未设置，则默认尝试推送到第一个运营通知群。</div>
              </el-form-item>

              <el-form-item label="运营通知群">
                <el-select v-model="botForm.notifyGroupIds" multiple filterable allow-create default-first-option
                           placeholder="选择接收运维通知的群号" style="width:100%">
                  <el-option v-for="g in bot.groups" :key="g.groupId" :label="g.groupName + ' (' + g.groupId + ')'" :value="g.groupId"></el-option>
                </el-select>
                <div class="muted">用于接收系统级运维、监控告警或广播通知的 QQ 群列表。</div>
              </el-form-item>

              <el-form-item label="运营私聊通知">
                <el-select v-model="botForm.notifyPrivateUserIds" multiple filterable allow-create default-first-option
                           placeholder="从好友选择或直接输入 QQ 号" style="width:100%">
                  <el-option v-for="f in bot.friends" :key="f.userId" :label="(f.remark || f.nickname) + ' (' + f.userId + ')'" :value="f.userId"></el-option>
                </el-select>
                <div class="muted">用于接收运营私聊通知的管理员 QQ 列表。</div>
              </el-form-item>

              <el-form-item>
                <el-button type="primary" :loading="bot.saving" @click="saveBotSettings">保存设置</el-button>
                <el-button :loading="bot.loading" @click="loadBotStatus" style="margin-left:12px">重新读取</el-button>
              </el-form-item>
            </el-form>
          </el-card>

          <!-- 已加入群列表展示 -->
          <el-card class="server-card">
            <template #header>
              <div style="display:flex;justify-content:space-between;align-items:center">
                <span style="font-weight:600">已加入的群聊列表（{{ bot.groups.length }}）</span>
                <span class="muted" v-if="bot.groups.length > 0">可快捷添加到白名单或设为推送群</span>
              </div>
            </template>

            <el-empty v-if="bot.groups.length === 0" description="暂无群组数据（机器人未连接或未加入任何群聊）"></el-empty>
            <el-table v-else :data="bot.groups" border stripe max-height="360">
              <el-table-column prop="groupId" label="群号" width="160"></el-table-column>
              <el-table-column prop="groupName" label="群名称"></el-table-column>
              <el-table-column label="成员数" width="130">
                <template #default="{ row }">
                  {{ row.memberCount }} / {{ row.maxMemberCount }}
                </template>
              </el-table-column>
              <el-table-column label="快捷操作" width="220">
                <template #default="{ row }">
                  <el-button link size="small" @click="addAllowedGroup(row.groupId)"
                             :disabled="botForm.allowedGroupIds.includes(row.groupId)">
                    {{ botForm.allowedGroupIds.includes(row.groupId) ? '已在白名单' : '+ 白名单' }}
                  </el-button>
                  <el-button link size="small" type="primary" @click="botForm.acTargetGroupId = row.groupId"
                             :disabled="botForm.acTargetGroupId === row.groupId">
                    {{ botForm.acTargetGroupId === row.groupId ? '已是.ac目标' : '设为.ac目标' }}
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </div>
`,
  setup() {
    const bot = useBot();
    return {
      ...bot,
      fmtTime,
    };
  }
};
