import { useBot } from '../modules/useBot.js';
import { fmtTime } from '../api.js';
import { ref, ElMessage, ElMessageBox } from '../deps.js';

export const BotView = {
  name: 'BotView',
  template: `
        <div>
          <div class="page-header">
            <div class="page-header-info">
              <h2>QQ 机器人</h2>
            </div>
            <div class="page-header-actions">
              <el-button size="small" @click="showCommands">官方指令</el-button>
              <el-button size="small" type="success" plain :disabled="!bot.status.isConnected" @click="openTestDialog">发送测试消息</el-button>
              <el-button size="small" type="primary" plain :loading="bot.reconnecting" @click="reconnectBot">↻ 重新连接</el-button>
              <el-button size="small" :loading="bot.loading" @click="loadBotStatus">刷新状态</el-button>
            </div>
          </div>

          <!-- 状态与实时连接指标 -->
          <el-card shadow="never" style="margin-bottom:18px">
            <template #header>
              <div class="section-card-head">
                <div class="section-card-title">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <rect x="3" y="11" width="18" height="10" rx="2"></rect>
                    <circle cx="12" cy="5" r="2"></circle>
                    <path d="M12 7v4"></path>
                    <line x1="8" y1="16" x2="8.01" y2="16"></line>
                    <line x1="16" y1="16" x2="16.01" y2="16"></line>
                  </svg>
                  <span>连接状态</span>
                  <el-tag type="info" effect="plain" size="small">{{ bot.status.platformDisplay || '-' }}</el-tag>
                  <el-tag :type="bot.status.isConnected ? 'success' : (bot.status.nextReconnectInSeconds > 0 ? 'warning' : 'danger')" effect="dark" size="small">
                    {{ bot.status.isConnected ? '已连接' : (bot.status.nextReconnectInSeconds > 0 ? ('重连中 (' + bot.status.nextReconnectInSeconds + 's)') : '未连接') }}
                  </el-tag>
                </div>
                <span class="muted" v-if="bot.configPath">配置文件：<code>{{ bot.configPath }}</code></span>
              </div>
            </template>

            <div class="metric-tiles">
              <div class="metric-tile">
                <div class="metric-tile-label">登录机器人身份</div>
                <div class="metric-tile-value">
                  {{ bot.status.connectedUserId ? (bot.status.connectedNickname || '机器人') : '未获取' }}
                </div>
                <div class="metric-tile-sub">ID：{{ bot.status.connectedUserId || '等待连接' }}</div>
              </div>
              <div class="metric-tile">
                <div class="metric-tile-label">当前通信接入点</div>
                <div class="metric-tile-value" style="font-size:13px">{{ bot.status.endpoint || '-' }}</div>
                <div class="metric-tile-sub">
                  可见群聊：{{ bot.groups.length }} 个{{ bot.status.supportsGroupListing ? '' : '（活跃群）' }}
                </div>
              </div>
              <div class="metric-tile">
                <div class="metric-tile-label">最近连接时间</div>
                <div class="metric-tile-value" style="font-size:13.5px">{{ fmtTime(bot.status.lastConnectedAt) }}</div>
                <div class="metric-tile-sub">上次断开：{{ fmtTime(bot.status.lastDisconnectedAt) }}</div>
              </div>
              <div class="metric-tile">
                <div class="metric-tile-label">自动重连状态</div>
                <div class="metric-tile-value">{{ bot.status.currentAttempt > 0 ? (bot.status.currentAttempt + ' 次重试') : '正常 (0 次)' }}</div>
              </div>
            </div>

            <el-alert v-if="bot.status.lastError" type="error" :closable="false" show-icon
                      :title="'最近连接错误：' + bot.status.lastError" style="margin-top:12px"></el-alert>
          </el-card>

          <!-- 设置表单 -->
          <el-card shadow="never" style="margin-bottom:18px">
            <template #header>
              <div class="section-card-head">
                <div class="section-card-title">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <circle cx="12" cy="12" r="3"></circle>
                    <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path>
                  </svg>
                  <span>机器人引擎与联动参数设置</span>
                </div>
                <div style="display:flex;gap:8px">
                  <el-button size="small" :loading="bot.loading" @click="loadBotStatus">重新读取</el-button>
                  <el-button size="small" type="primary" :loading="bot.saving" @click="saveBotSettings">保存设置</el-button>
                </div>
              </div>
            </template>

            <el-form label-width="160px" style="max-width:940px">
              <h4 style="margin:0 0 12px;color:var(--qb-accent)">一、选择接入模式</h4>
              <el-form-item label="接入引擎">
                <div class="mode-selector-grid">
                  <div class="mode-option-card" :class="{ active: botForm.mode === 'NapCat' }" @click="botForm.mode = 'NapCat'">
                    <div class="mode-option-head">
                      <span>NapCat / OneBot 11</span>
                      <el-tag size="small" :type="botForm.mode === 'NapCat' ? 'primary' : 'info'">自建客户端</el-tag>
                    </div>
                    <div class="mode-option-desc">连接本机或局域网 NapCatQQ 正向 WebSocket，直接获取真实 QQ 号与群列表，无主动消息配额限制。</div>
                  </div>
                  <div class="mode-option-card" :class="{ active: botForm.mode === 'OfficialQq' }" @click="botForm.mode = 'OfficialQq'">
                    <div class="mode-option-head">
                      <span>QQ 官方 Bot API v2</span>
                      <el-tag size="small" :type="botForm.mode === 'OfficialQq' ? 'primary' : 'info'">开放平台</el-tag>
                    </div>
                    <div class="mode-option-desc">直连腾讯 QQ 开放平台网关，群与用户使用 OpenID 标识，支持同步手机/电脑端快捷指令面板。</div>
                  </div>
                </div>
                <div class="muted">
                  切换并保存后由程序自动停用旧连接、启用新连接。同一时刻只有一套通道工作，避免重复回复。
                </div>
              </el-form-item>

              <el-divider></el-divider>

              <!-- NapCat -->
              <template v-if="botForm.mode === 'NapCat'">
                <h4 style="margin:0 0 14px;color:var(--qb-accent)">二、OneBot 11 (NapCatQQ) 连接参数</h4>
                <el-form-item label="正向 WS 地址">
                  <el-input v-model="botForm.wsBaseUri" placeholder="ws://127.0.0.1:6700"></el-input>
                  <div class="muted">NapCatQQ 的正向 WebSocket 接口地址（需以 ws:// 或 wss:// 开头）</div>
                </el-form-item>
                <el-form-item label="基础重连间隔">
                  <el-input-number v-model="botForm.reconnectDelaySeconds" :min="1" :max="300" style="width:160px"></el-input-number>
                  <span class="muted" style="margin-left:8px">秒（连接断开后首次等待时长）</span>
                </el-form-item>
                <el-form-item label="最大重连间隔">
                  <el-input-number v-model="botForm.maxReconnectDelaySeconds" :min="1" :max="3600" style="width:160px"></el-input-number>
                  <span class="muted" style="margin-left:8px">秒（连续重试指数退避上限）</span>
                </el-form-item>
              </template>

              <!-- 官方 -->
              <template v-else>
                <h4 style="margin:0 0 14px;color:var(--qb-accent)">二、QQ 开放平台凭证</h4>
                <el-form-item label="开放平台 AppID">
                  <el-input v-model="botForm.officialAppId" placeholder="在 q.qq.com → 开发设置 中获取"></el-input>
                </el-form-item>
                <el-form-item label="AppSecret">
                  <el-input v-model="botForm.officialClientSecret" type="password" show-password
                            :placeholder="bot.hasClientSecret ? '已保存（留空表示不修改）' : '请输入 AppSecret'"></el-input>
                  <div class="muted">密钥不会回显到面板，仅在保存时写入配置文件。</div>
                </el-form-item>
                <el-form-item label="API 根地址">
                  <el-input v-model="botForm.officialApiBase" placeholder="https://api.bot.qq.com"></el-input>
                  <div class="muted">生产环境 https://api.bot.qq.com（旧域名 https://api.sgroup.qq.com）</div>
                </el-form-item>
                <el-form-item label="沙箱环境">
                  <el-switch v-model="botForm.officialSandbox"></el-switch>
                  <span class="muted" style="margin-left:8px">开启后自动在主机名前加 sandbox. 前缀</span>
                </el-form-item>
                <el-form-item label="凭证在线校验">
                  <el-button :loading="bot.testingAuth" @click="testOfficialAuth">测试凭证有效性</el-button>
                  <span class="muted" style="margin-left:8px">
                    只申请一次 AccessToken 验证 AppID / AppSecret，不切换模式、不保存配置。
                  </span>
                </el-form-item>
                <el-form-item label="订阅事件 Intents">
                  <el-input-number v-model="botForm.officialIntents" :min="1" style="width:200px"></el-input-number>
                  <span class="muted" style="margin-left:8px">默认 33554432（1&lt;&lt;25，群聊 @ 与单聊消息）</span>
                </el-form-item>
                <el-form-item label="单条文本上限">
                  <el-input-number v-model="botForm.officialMaxTextLength" :min="100" :max="4000" :step="100" style="width:160px"></el-input-number>
                  <span class="muted" style="margin-left:8px">字符；超长回复会自动分段并递增 msg_seq</span>
                </el-form-item>
                <el-form-item label="允许主动推送">
                  <el-switch v-model="botForm.officialAllowActivePush"></el-switch>
                  <span class="muted" style="margin-left:8px">
                    官方对主动消息有配额限制。关闭时仅能被动回复用户消息，游戏内 .ac 推送会被跳过。
                  </span>
                </el-form-item>

                <el-divider></el-divider>

                <h4 style="margin:0 0 14px;color:var(--qb-accent)">三、官方平台权限与目标 (OpenID)</h4>
                <el-form-item label="管理员 OpenID">
                  <el-select v-model="botForm.officialAdminOpenIds" multiple filterable allow-create default-first-option
                             placeholder="输入 openid 后回车。配置后群主或管理员身份不会自动放行" style="width:100%"></el-select>
                  <div class="muted">
                    配置后管理指令只认这份名单，不看群角色。留空时沿用原有判定：群聊中群主和管理员可用，单聊不放行。
                  </div>
                </el-form-item>
                <el-form-item label="群消息白名单">
                  <el-select v-model="botForm.officialAllowedGroupOpenIds" multiple filterable allow-create default-first-option
                             placeholder="输入 group_openid 后回车" style="width:100%"></el-select>
                  <div class="muted">留空表示不限制。官方平台无法获取群名称，只能凭 OpenID 识别。</div>
                </el-form-item>
                <el-form-item label="运营通知群">
                  <el-select v-model="botForm.officialNotifyGroupOpenIds" multiple filterable allow-create default-first-option
                             placeholder="输入 group_openid 后回车" style="width:100%"></el-select>
                  <div class="muted">用于接收运维通知的群（需同时开启「允许主动推送」）。</div>
                </el-form-item>
                <el-form-item label=".ac 推送目标群">
                  <el-input v-model="botForm.officialAcTargetGroupOpenId" placeholder="group_openid，留空则回退到通知群"></el-input>
                </el-form-item>
                <el-form-item label="运营私聊通知">
                  <el-select v-model="botForm.officialNotifyPrivateOpenIds" multiple filterable allow-create default-first-option
                             placeholder="输入 user_openid 后回车" style="width:100%"></el-select>
                </el-form-item>

                <el-divider></el-divider>

                <h4 style="margin:0 0 14px;color:var(--qb-accent)">四、QQ 官方指令面板（/v2/panels 自动创建与管理）</h4>
                <div class="muted" style="margin-bottom:14px;line-height:1.6">
                  指令面板以弹窗菜单形式在手机 QQ 与电脑 QQ 聊天输入框快捷栏中展示机器人指令。
                  点击「一键同步」将自动在 QQ 开放平台创建/更新<b>群聊全局指令面板</b>（11 项，管理指令限管理员）与<b>单聊全局指令面板</b>（7 项普通指令）。
                </div>

                <div style="margin-bottom:14px;display:flex;gap:10px;align-items:center;flex-wrap:wrap">
                  <el-button type="primary" :loading="bot.syncingPanels" @click="syncOfficialPanels">
                    ⚡ 一键同步指令面板到 QQ 官方
                  </el-button>
                  <el-button :loading="bot.loadingPanels" @click="loadOfficialPanels">
                    刷新面板列表
                  </el-button>
                </div>

                <div v-if="bot.panels && bot.panels.length > 0" style="display:flex;flex-direction:column;gap:12px;margin-bottom:16px">
                  <div v-for="p in bot.panels" :key="p.panel_id" style="padding:14px 16px;background:var(--qb-surface-2);border:1px solid var(--qb-border);border-radius:10px">
                    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px;flex-wrap:wrap;gap:8px">
                      <div style="font-weight:600;display:flex;align-items:center;gap:8px">
                        <el-tag :type="p.scope === 'group' ? 'primary' : 'success'" size="small">
                          {{ p.scope === 'group' ? '群聊面板' : (p.scope === 'c2c' ? '单聊面板' : p.scope) }}
                        </el-tag>
                        <span>{{ p.panel?.remark || p.panel_id }}</span>
                        <span class="muted" style="font-size:12px">({{ p.target_type === 'all' ? '全局生效' : '指定对象' }} · v{{ p.version }})</span>
                      </div>
                      <el-button size="small" type="danger" plain :loading="bot.deletingPanelId === p.panel_id" @click="confirmDeletePanel(p.panel_id)">
                        删除面板
                      </el-button>
                    </div>
                    <div class="muted" style="font-size:12px;margin-bottom:10px">
                      ID: <code>{{ p.panel_id }}</code>
                      <span v-if="p.updated_at" style="margin-left:12px">更新时间: {{ fmtTime(p.updated_at) }}</span>
                    </div>
                    <div style="display:flex;flex-wrap:wrap;gap:6px">
                      <el-tag v-for="item in (p.panel?.items || [])" :key="item.name" size="small" :type="item.only_admin ? 'warning' : 'info'">
                        {{ item.name }}
                        <span v-if="item.only_admin" style="font-size:10px;margin-left:2px">(管理)</span>
                        <span style="font-size:11px;opacity:0.85;margin-left:4px">{{ item.desc }}</span>
                      </el-tag>
                    </div>
                  </div>
                </div>
                <div v-else-if="!bot.loadingPanels" class="muted" style="margin-bottom:16px">
                  尚未在 QQ 开放平台创建指令面板，可点击上方「一键同步」按钮自动创建。
                </div>
              </template>

              <el-divider></el-divider>

              <h4 style="margin:0 0 14px;color:var(--qb-accent)">
                {{ botForm.mode === 'NapCat' ? '三' : '五' }}、群组白名单与推送通知（NapCat 模式）
              </h4>
              <template v-if="botForm.mode === 'NapCat'">
                <el-form-item label="群消息白名单">
                  <el-select v-model="botForm.allowedGroupIds" multiple filterable allow-create default-first-option
                             placeholder="从已加入群组选择或直接输入群号" style="width:100%">
                    <el-option v-for="g in bot.groups" :key="g.groupId" :label="g.groupName + ' (' + g.groupId + ')'" :value="g.groupId"></el-option>
                  </el-select>
                  <div class="muted">仅响应白名单内群聊的指令。留空则监听机器人所在的所有群聊。</div>
                </el-form-item>

                <el-form-item label="管理指令 QQ 白名单">
                  <el-select v-model="botForm.adminUserIds" multiple filterable allow-create default-first-option
                             placeholder="从好友选择或直接输入 QQ 号" style="width:100%">
                    <el-option v-for="f in bot.friends" :key="'admin-' + f.userId" :label="(f.remark || f.nickname) + ' (' + f.userId + ')'" :value="f.userId"></el-option>
                  </el-select>
                  <div class="muted">配置后只有名单内的 QQ 能使用封禁、广播、回合等管理指令。留空时沿用群主/群管理员判定。</div>
                </el-form-item>

                <el-form-item label=".ac 游戏推送群">
                  <el-select v-model="botForm.acTargetGroupId" filterable allow-create default-first-option
                             placeholder="选择接收游戏内 .ac 推送的群" style="width:100%">
                    <el-option :value="0" label="未设置（不单独推送）"></el-option>
                    <el-option v-for="g in bot.groups" :key="g.groupId" :label="g.groupName + ' (' + g.groupId + ')'" :value="g.groupId"></el-option>
                  </el-select>
                  <div class="muted">玩家在游戏内输入 <code>.ac 消息</code> 时接收实时告警推送的群。</div>
                </el-form-item>

                <el-form-item label="运营通知群">
                  <el-select v-model="botForm.notifyGroupIds" multiple filterable allow-create default-first-option
                             placeholder="选择接收运维通知的群号" style="width:100%">
                    <el-option v-for="g in bot.groups" :key="g.groupId" :label="g.groupName + ' (' + g.groupId + ')'" :value="g.groupId"></el-option>
                  </el-select>
                </el-form-item>

                <el-form-item label="运营私聊通知">
                  <el-select v-model="botForm.notifyPrivateUserIds" multiple filterable allow-create default-first-option
                             placeholder="从好友选择或直接输入 QQ 号" style="width:100%">
                    <el-option v-for="f in bot.friends" :key="f.userId" :label="(f.remark || f.nickname) + ' (' + f.userId + ')'" :value="f.userId"></el-option>
                  </el-select>
                </el-form-item>
              </template>
              <template v-else>
                <div class="muted" style="margin-bottom:12px">
                  官方平台的群组与用户均为 OpenID，相关配置请在上方「三、官方平台权限与目标」中填写。
                </div>
              </template>

              <el-form-item>
                <el-button type="primary" :loading="bot.saving" @click="saveBotSettings">保存并应用设置</el-button>
                <el-button :loading="bot.loading" @click="loadBotStatus" style="margin-left:10px">重新读取</el-button>
              </el-form-item>
            </el-form>
          </el-card>

          <!-- 群列表 -->
          <el-card shadow="never">
            <template #header>
              <div class="section-card-head">
                <div class="section-card-title">
                  <span>{{ bot.status.supportsGroupListing ? '已加入的 QQ 群聊列表' : '观察到活跃的群（官方平台）' }} ({{ bot.groups.length }})</span>
                </div>
              </div>
            </template>

            <el-alert v-if="!bot.status.supportsGroupListing" type="warning" :closable="false" show-icon
                      title="官方平台不提供群列表接口，这里展示的是机器人曾被 @ 过的活跃群；其 OpenID 可直接用于白名单与通知配置。"
                      style="margin-bottom:12px"></el-alert>

            <el-empty v-if="bot.groups.length === 0" description="暂无群组数据（机器人未连接或未加入任何群聊）"></el-empty>
            <el-table v-else :data="bot.groups" border stripe max-height="380">
              <el-table-column prop="groupId" :label="bot.status.supportsGroupListing ? '群号' : '群 OpenID'" min-width="200">
                <template #default="{ row }">
                  <code>{{ row.groupId }}</code>
                </template>
              </el-table-column>
              <el-table-column prop="groupName" label="群名称" min-width="180"></el-table-column>
              <el-table-column label="成员数" width="130" v-if="bot.status.supportsGroupListing">
                <template #default="{ row }">
                  <span class="mono">{{ row.memberCount }} / {{ row.maxMemberCount }}</span>
                </template>
              </el-table-column>
              <el-table-column label="快捷操作" width="280" fixed="right">
                <template #default="{ row }">
                  <template v-if="bot.status.supportsGroupListing">
                    <el-button link size="small" @click="addAllowedGroup(row.groupId)"
                               :disabled="botForm.allowedGroupIds.includes(row.groupId)">
                      {{ botForm.allowedGroupIds.includes(row.groupId) ? '✓ 已在白名单' : '+ 加入白名单' }}
                    </el-button>
                    <el-button link size="small" type="primary" @click="botForm.acTargetGroupId = row.groupId"
                               :disabled="botForm.acTargetGroupId === row.groupId">
                      {{ botForm.acTargetGroupId === row.groupId ? '✓ 已是 .ac 目标' : '设为 .ac 目标' }}
                    </el-button>
                  </template>
                  <template v-else>
                    <el-button link size="small" @click="addOfficialGroupOpenId(row.groupId)"
                               :disabled="botForm.officialAllowedGroupOpenIds.includes(row.groupId)">
                      {{ botForm.officialAllowedGroupOpenIds.includes(row.groupId) ? '✓ 已在白名单' : '+ 加入白名单' }}
                    </el-button>
                    <el-button link size="small" type="primary" @click="addOfficialNotifyGroup(row.groupId)"
                               :disabled="botForm.officialNotifyGroupOpenIds.includes(row.groupId)">
                      {{ botForm.officialNotifyGroupOpenIds.includes(row.groupId) ? '✓ 已是通知群' : '设为通知群' }}
                    </el-button>
                  </template>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <!-- 官方管理端指令清单 -->
          <el-dialog v-model="commandDialog" title="QQ 开放平台管理端 · 指令配置清单" width="720px">
            <div class="muted" style="margin-bottom:10px">
              在 QQ 开放平台管理端「指令配置」中按下列内容登记，玩家即可通过指令面板快捷触发。
              官方限制：指令名 ≤8 个中文字符或 16 个英文字符，指令介绍 ≤15 个中文字符或 30 个英文字符，最多 24 条。
            </div>
            <el-input v-model="commandText" type="textarea" :rows="18" readonly class="mono"></el-input>
          </el-dialog>
        </div>
`,
  setup() {
    const api = useBot();
    const commandDialog = ref(false);
    const commandText = ref('');

    async function showCommands() {
      commandText.value = await api.loadCommandManifest();
      commandDialog.value = true;
    }

    async function confirmDeletePanel(panelId) {
      try {
        await ElMessageBox.confirm(`确定要从 QQ 开放平台删除指令面板「${panelId}」吗？删除后该面板不再对任何用户或群生效。`, '确认删除', {
          confirmButtonText: '确定删除',
          cancelButtonText: '取消',
          type: 'warning',
        });
        await api.deleteOfficialPanel(panelId);
      } catch (e) {
        // 用户取消
      }
    }

    return {
      ...api,
      fmtTime,
      ElMessage,
      commandDialog,
      commandText,
      showCommands,
      confirmDeletePanel,
    };
  }
};
