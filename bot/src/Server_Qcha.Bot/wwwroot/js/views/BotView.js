import { useBot } from '../modules/useBot.js';
import { fmtTime } from '../api.js';
import { ref, ElMessage, ElMessageBox } from '../deps.js';

export const BotView = {
  name: 'BotView',
  template: `
        <div>
          <el-alert type="info" :closable="false" show-icon
                    title="QQ 机器人运行与联动管理" style="margin-bottom:12px">
            <div>支持两种接入方式，可在下方「接入模式」中在线切换，保存后立即重建连接，无需重启程序。</div>
            <div style="margin-top:4px">
              <b>NapCat / OneBot 11</b>：连接本机 NapCatQQ 正向 WebSocket，可拿到真实群号与 QQ 号。
              <b>QQ 官方 Bot API</b>：直连 QQ 开放平台，群与用户均为 OpenID，群聊中仅能收到 @机器人 的消息。
            </div>
          </el-alert>

          <!-- 状态与控制栏 -->
          <el-card class="server-card" style="margin-bottom:16px">
            <div class="server-head">
              <div style="display:flex;align-items:center;gap:10px">
                <span style="font-weight:600;font-size:16px">连接状态</span>
                <el-tag type="info" effect="plain">{{ bot.status.platformDisplay || '-' }}</el-tag>
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
              <el-descriptions-item label="登录身份">
                <template v-if="bot.status.connectedUserId">
                  <b>{{ bot.status.connectedNickname || '机器人' }}</b>
                  <span class="muted"> ({{ bot.status.connectedUserId }})</span>
                </template>
                <span v-else class="muted">（未获取）</span>
              </el-descriptions-item>
              <el-descriptions-item label="接入点">{{ bot.status.endpoint || '-' }}</el-descriptions-item>
              <el-descriptions-item label="可见群聊">
                <template v-if="bot.status.supportsGroupListing">{{ bot.groups.length }} 个群</template>
                <template v-else>{{ bot.groups.length }} 个（官方平台仅记录活跃群）</template>
              </el-descriptions-item>
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

            <el-form label-width="170px" style="max-width:880px">
              <h4 style="margin:0 0 14px;color:var(--qb-accent)">一、接入模式</h4>
              <el-form-item label="当前模式">
                <el-select v-model="botForm.mode" style="width:320px">
                  <el-option value="NapCat" label="NapCat / OneBot 11（自建 QQ 客户端）"></el-option>
                  <el-option value="OfficialQq" label="QQ 官方 Bot API（开放平台）"></el-option>
                </el-select>
                <div class="muted">
                  切换后由程序自动停用旧连接、启用新连接。同一时刻只有一套通道在工作，避免重复回复。
                </div>
              </el-form-item>

              <el-form-item label="指令清单">
                <el-button size="small" @click="showCommands">查看官方管理端指令配置清单</el-button>
                <span class="muted" style="margin-left:8px">官方要求在机器人后台登记指令，名称不超过 8 个中文字符 / 16 个英文字符</span>
              </el-form-item>

              <el-divider></el-divider>

              <!-- NapCat -->
              <template v-if="botForm.mode === 'NapCat'">
                <h4 style="margin:0 0 14px;color:var(--qb-accent)">二、OneBot 11 (NapCatQQ) 连接</h4>
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
                <el-form-item label="凭证校验">
                  <el-button :loading="bot.testingAuth" @click="testOfficialAuth">测试凭证</el-button>
                  <span class="muted" style="margin-left:8px">
                    只申请一次 AccessToken 验证 AppID / AppSecret，不切换模式、不保存配置。
                    AppSecret 留空则用已保存的那个。
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
                    官方对主动消息有配额限制。关闭时仅能回复用户消息（被动回复），游戏内 .ac 推送会被跳过。
                  </span>
                </el-form-item>

                <el-divider></el-divider>

                <h4 style="margin:0 0 14px;color:var(--qb-accent)">三、官方平台权限与目标</h4>
                <el-form-item label="管理员 OpenID">
                  <el-select v-model="botForm.officialAdminOpenIds" multiple filterable allow-create default-first-option
                             placeholder="输入 openid 后回车；群主/管理员自动识别" style="width:100%"></el-select>
                  <div class="muted">
                    群聊中优先使用官方下发的 member_role 判定；该列表作为兜底。单聊场景必须在此登记才可执行管理指令。
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

                <h4 style="margin:0 0 14px;color:var(--qb-accent)">四、QQ 官方指令面板（自动创建与管理）</h4>
                <div class="muted" style="margin-bottom:14px;line-height:1.6">
                  指令面板以弹窗菜单形式在手机 QQ 与电脑 QQ 的聊天输入框快捷栏中展示机器人的指令。
                  点击后指令自动填入输入框，无需手动记忆命令。
                  点击「一键同步」将根据指令库自动在 QQ 开放平台创建/更新<b>群聊全局指令面板</b>（11 项，管理指令限管理员）与<b>单聊全局指令面板</b>（7 项普通指令）。
                </div>

                <div style="margin-bottom:14px;display:flex;gap:10px;align-items:center">
                  <el-button type="primary" :loading="bot.syncingPanels" @click="syncOfficialPanels">
                    一键同步指令面板到 QQ 官方
                  </el-button>
                  <el-button :loading="bot.loadingPanels" @click="loadOfficialPanels">
                    刷新指令面板列表
                  </el-button>
                </div>

                <div v-if="bot.panels && bot.panels.length > 0" style="display:flex;flex-direction:column;gap:12px;margin-bottom:16px">
                  <div v-for="p in bot.panels" :key="p.panel_id" class="kpi-card" style="padding:14px;border:1px solid var(--qb-border);border-radius:6px">
                    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px">
                      <div style="font-weight:bold;display:flex;align-items:center;gap:8px">
                        <el-tag :type="p.scope === 'group' ? 'primary' : 'success'" size="small">
                          {{ p.scope === 'group' ? '群聊面板' : (p.scope === 'c2c' ? '单聊面板' : p.scope) }}
                        </el-tag>
                        <span>{{ p.panel?.remark || p.panel_id }}</span>
                        <span class="muted" style="font-size:12px">({{ p.target_type === 'all' ? '全局生效' : '指定对象' }} · 版本 v{{ p.version }})</span>
                      </div>
                      <el-button size="small" type="danger" plain :loading="bot.deletingPanelId === p.panel_id" @click="confirmDeletePanel(p.panel_id)">
                        删除面板
                      </el-button>
                    </div>
                    <div class="muted" style="font-size:12px;margin-bottom:8px">
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

                <el-form-item label=".ac 游戏推送群">
                  <el-select v-model="botForm.acTargetGroupId" filterable allow-create default-first-option
                             placeholder="选择接收游戏内 .ac 推送的群" style="width:100%">
                    <el-option :value="0" label="未设置（不单独推送）"></el-option>
                    <el-option v-for="g in bot.groups" :key="g.groupId" :label="g.groupName + ' (' + g.groupId + ')'" :value="g.groupId"></el-option>
                  </el-select>
                  <div class="muted">玩家在游戏内输入 <code>.ac 消息</code> 时接收实时推送的群。</div>
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
                  <div class="muted">私聊指令仅对名单内的 QQ 生效，避免陌生人触发管理操作。</div>
                </el-form-item>
              </template>
              <template v-else>
                <div class="muted" style="margin-bottom:12px">
                  官方平台的群组与用户均为 OpenID，相关配置请在上方「三、官方平台权限与目标」中填写。
                </div>
              </template>

              <el-form-item>
                <el-button type="primary" :loading="bot.saving" @click="saveBotSettings">保存设置</el-button>
                <el-button :loading="bot.loading" @click="loadBotStatus" style="margin-left:12px">重新读取</el-button>
              </el-form-item>
            </el-form>
          </el-card>

          <!-- 群列表 -->
          <el-card class="server-card">
            <template #header>
              <div style="display:flex;justify-content:space-between;align-items:center">
                <span style="font-weight:600">
                  {{ bot.status.supportsGroupListing ? '已加入的群聊列表' : '观察到活跃的群（官方平台）' }}（{{ bot.groups.length }}）
                </span>
                <span class="muted" v-if="bot.groups.length > 0">可快捷添加到白名单或设为推送群</span>
              </div>
            </template>

            <el-alert v-if="!bot.status.supportsGroupListing" type="warning" :closable="false" show-icon
                      title="官方平台不提供群列表接口，这里展示的是机器人曾被 @ 过的群；其 OpenID 可直接用于白名单与通知配置。"
                      style="margin-bottom:10px"></el-alert>

            <el-empty v-if="bot.groups.length === 0" description="暂无群组数据（机器人未连接或未加入任何群聊）"></el-empty>
            <el-table v-else :data="bot.groups" border stripe max-height="360">
              <el-table-column prop="groupId" :label="bot.status.supportsGroupListing ? '群号' : '群 OpenID'" min-width="200"></el-table-column>
              <el-table-column prop="groupName" label="群名称"></el-table-column>
              <el-table-column label="成员数" width="120" v-if="bot.status.supportsGroupListing">
                <template #default="{ row }">
                  {{ row.memberCount }} / {{ row.maxMemberCount }}
                </template>
              </el-table-column>
              <el-table-column label="快捷操作" width="300">
                <template #default="{ row }">
                  <template v-if="bot.status.supportsGroupListing">
                    <el-button link size="small" @click="addAllowedGroup(row.groupId)"
                               :disabled="botForm.allowedGroupIds.includes(row.groupId)">
                      {{ botForm.allowedGroupIds.includes(row.groupId) ? '已在白名单' : '+ 白名单' }}
                    </el-button>
                    <el-button link size="small" type="primary" @click="botForm.acTargetGroupId = row.groupId"
                               :disabled="botForm.acTargetGroupId === row.groupId">
                      {{ botForm.acTargetGroupId === row.groupId ? '已是.ac目标' : '设为.ac目标' }}
                    </el-button>
                  </template>
                  <template v-else>
                    <el-button link size="small" @click="addOfficialGroupOpenId(row.groupId)"
                               :disabled="botForm.officialAllowedGroupOpenIds.includes(row.groupId)">
                      {{ botForm.officialAllowedGroupOpenIds.includes(row.groupId) ? '已在白名单' : '+ 白名单' }}
                    </el-button>
                    <el-button link size="small" type="primary" @click="addOfficialNotifyGroup(row.groupId)"
                               :disabled="botForm.officialNotifyGroupOpenIds.includes(row.groupId)">
                      {{ botForm.officialNotifyGroupOpenIds.includes(row.groupId) ? '已是通知群' : '设为通知群' }}
                    </el-button>
                  </template>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <!-- 官方管理端指令清单 -->
          <el-dialog v-model="commandDialog" title="QQ 开放平台管理端 · 指令配置清单" width="720px">
            <div class="muted" style="margin-bottom:8px">
              在 QQ 开放平台管理端「指令配置」中按下列内容登记，玩家即可通过指令面板快捷触发。
              官方限制：指令名 ≤8 个中文字符或 16 个英文字符，指令介绍 ≤15 个中文字符或 30 个英文字符，最多 24 条。
            </div>
            <el-input v-model="commandText" type="textarea" :rows="18" readonly></el-input>
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
