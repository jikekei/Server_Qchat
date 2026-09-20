import { useServers } from '../modules/useServers.js';
import { useAccounts } from '../modules/useAccounts.js';
import { useLocalAdmin } from '../modules/useLocalAdmin.js';
import { useDatabase } from '../modules/useDatabase.js';
import { useBot } from '../modules/useBot.js';
import { can, fmtTime, meta, randomPassword } from '../api.js';

export const AppDialogs = {
  name: 'AppDialogs',
  template: `
  <div>
  <!-- ==================== 玩家列表 ==================== -->
  <el-dialog v-model="playersDialog.visible" :title="playersDialog.title" width="640px">
    <div class="toolbar">
      <el-button size="small" :loading="playersDialog.loading" @click="reloadPlayers">刷新</el-button>
      <span class="muted">共 {{ playersDialog.players.length }} 名玩家</span>
    </div>
    <el-table :data="playersDialog.players" border stripe max-height="380">
      <el-table-column prop="id" label="游戏内 ID" width="110"></el-table-column>
      <el-table-column prop="name" label="昵称"></el-table-column>
      <el-table-column label="操作" width="150">
        <template #default="{ row }">
          <el-button link size="small" v-if="can('players.kick')" @click="openPunish('kick', row)">踢出</el-button>
          <el-button link size="small" type="danger" v-if="can('players.ban')" @click="openPunish('ban', row)">封禁</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-alert
      v-if="!can('players.kick') && !can('players.ban')"
      type="info" :closable="false" show-icon
      title="当前账号没有踢出/封禁权限" style="margin-top:12px"></el-alert>
  </el-dialog>

  <!-- ==================== 服务器详情 ==================== -->
  <el-dialog v-model="infoDialog.visible" :title="infoDialog.title" width="620px">
    <div class="pre-box"><div class="mono">{{ infoDialog.text || '（无返回）' }}</div></div>
  </el-dialog>

  <!-- ==================== 广播 ==================== -->
  <el-dialog v-model="broadcastDialog.visible" :title="broadcastDialog.title" width="520px">
    <el-form label-width="70px">
      <el-form-item label="内容">
        <el-input v-model="broadcastDialog.message" type="textarea" :rows="4" maxlength="200" show-word-limit
                  placeholder="将显示在所有玩家屏幕上（15 秒）"></el-input>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="broadcastDialog.visible = false">取消</el-button>
      <el-button type="primary" :loading="broadcastDialog.loading" @click="submitBroadcast">发送</el-button>
    </template>
  </el-dialog>

  <!-- ==================== QQ 机器人测试消息 ==================== -->
  <el-dialog v-model="testDialog.visible" title="发送 QQ 测试消息" width="520px">
    <el-form label-width="90px">
      <el-form-item label="目标类型">
        <el-radio-group v-model="testDialog.isGroup">
          <el-radio :value="true">群聊</el-radio>
          <el-radio :value="false">私聊（好友）</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="目标群" v-if="testDialog.isGroup">
        <el-select v-model="testDialog.targetId" filterable allow-create default-first-option placeholder="选择群聊或直接输入群号" style="width:100%">
          <el-option v-for="g in bot.groups" :key="g.groupId" :label="g.groupName + ' (' + g.groupId + ')'" :value="g.groupId"></el-option>
        </el-select>
      </el-form-item>
      <el-form-item label="目标好友" v-else>
        <el-select v-model="testDialog.targetId" filterable allow-create default-first-option placeholder="选择好友或直接输入 QQ 号" style="width:100%">
          <el-option v-for="f in bot.friends" :key="f.userId" :label="(f.remark || f.nickname) + ' (' + f.userId + ')'" :value="f.userId"></el-option>
        </el-select>
      </el-form-item>
      <el-form-item label="消息内容">
        <el-input v-model="testDialog.message" type="textarea" :rows="3" maxlength="500" show-word-limit placeholder="输入测试消息文本"></el-input>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="testDialog.visible = false">取消</el-button>
      <el-button type="primary" :loading="testDialog.loading" @click="submitTestMessage">发送</el-button>
    </template>
  </el-dialog>

  <!-- ==================== 踢出 / 封禁 ==================== -->
  <el-dialog v-model="punishDialog.visible" :title="punishDialog.title" width="520px">
    <el-form label-width="90px">
      <el-form-item label="玩家">
        <el-input :model-value="punishDialog.playerName + ' (ID ' + punishDialog.playerId + ')'" disabled></el-input>
      </el-form-item>
      <el-form-item label="时长">
        <el-select v-model="punishDialog.duration" style="width:100%">
          <el-option v-for="opt in durationOptions" :key="opt.value" :label="opt.label" :value="opt.value"></el-option>
        </el-select>
      </el-form-item>
      <el-form-item label="原因">
        <el-input v-model="punishDialog.reason" placeholder="填写原因" maxlength="80" show-word-limit></el-input>
      </el-form-item>
    </el-form>
    <el-alert
      type="warning" :closable="false" show-icon
      title="服务端以封禁实现，'踢出' 也会产生短期封禁记录"
      style="margin-bottom:4px"></el-alert>
    <template #footer>
      <el-button @click="punishDialog.visible = false">取消</el-button>
      <el-button type="primary" :loading="punishDialog.loading" @click="submitPunish">确认</el-button>
    </template>
  </el-dialog>

  <!-- ==================== 账号编辑 ==================== -->
  <el-dialog v-model="accountDialog.visible" :title="accountDialog.id ? '编辑账号' : '新建账号'" width="620px">
    <el-form label-width="90px">
      <el-form-item label="用户名" v-if="!accountDialog.id">
        <el-input v-model="accountDialog.username" placeholder="3-32 个字符"></el-input>
      </el-form-item>
      <el-form-item label="显示名">
        <el-input v-model="accountDialog.displayName" placeholder="留空则与用户名相同"></el-input>
      </el-form-item>
      <el-form-item label="初始密码" v-if="!accountDialog.id">
        <div style="display:flex;gap:8px;width:100%">
          <el-input v-model="accountDialog.password" placeholder="至少 8 位" style="flex:1"></el-input>
          <el-button @click="accountDialog.password = randomPassword()">随机</el-button>
        </div>
      </el-form-item>
      <el-form-item label="快速预设">
        <el-button v-for="p in meta.presets" :key="p.key" size="small" @click="applyPreset(p)">{{ p.name }}</el-button>
        <el-button size="small" @click="accountDialog.permissionKeys = []">清空</el-button>
      </el-form-item>
      <el-form-item label="权限">
        <el-checkbox-group v-model="accountDialog.permissionKeys">
          <div class="perm-list">
            <div class="perm-item" v-for="p in meta.permissions" :key="p.key">
              <el-checkbox :value="p.key">{{ p.name }}</el-checkbox>
              <span class="perm-desc">{{ p.description }}</span>
            </div>
          </div>
        </el-checkbox-group>
      </el-form-item>
      <el-form-item label="启用">
        <el-switch v-model="accountDialog.isEnabled" :disabled="accountDialog.isBuiltIn"></el-switch>
        <span v-if="accountDialog.isBuiltIn" class="muted" style="margin-left:12px">内置账号不可禁用</span>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="accountDialog.visible = false">取消</el-button>
      <el-button type="primary" :loading="accountDialog.loading" @click="saveAccount">保存</el-button>
    </template>
  </el-dialog>

  <!-- ==================== 修改自己密码 ==================== -->
  <el-dialog v-model="passwordDialog.visible" title="修改密码" width="460px">
    <el-form label-width="90px">
      <el-form-item label="当前密码">
        <el-input v-model="passwordDialog.current" type="password" show-password></el-input>
      </el-form-item>
      <el-form-item label="新密码">
        <el-input v-model="passwordDialog.next" type="password" show-password placeholder="至少 8 位"></el-input>
      </el-form-item>
    </el-form>
    <el-alert
      type="info" :closable="false" show-icon
      title="若开启了启动重置密码，下次重启后仍会随机生成新密码"
      style="margin-bottom:4px"></el-alert>
    <template #footer>
      <el-button @click="passwordDialog.visible = false">取消</el-button>
      <el-button type="primary" :loading="passwordDialog.loading" @click="submitPassword">保存</el-button>
    </template>
  </el-dialog>

  <!-- ==================== 服务器配置（LocalAdmin） ==================== -->
  <el-dialog v-model="serverDialog.visible"
             :title="serverDialog.isEdit ? '编辑服务器' : '添加服务器'" width="720px">
    <el-alert v-if="serverDialog.warning" type="warning" :closable="false" show-icon
              :title="serverDialog.warning" style="margin-bottom:12px"></el-alert>

    <el-form label-width="110px">
      <el-form-item label="名称">
        <el-input v-model="serverDialog.form.name" placeholder="面板上显示的名字，例如「主服 31440」"></el-input>
      </el-form-item>

      <el-form-item label="标识（Id）">
        <el-input v-model="serverDialog.form.id" :disabled="serverDialog.isEdit"
                  placeholder="接口路径用，留空按名称自动生成"></el-input>
        <span class="muted" v-if="serverDialog.isEdit">编辑时不可修改，避免接口路径失效</span>
      </el-form-item>

      <el-form-item label="可执行文件">
        <div style="display:flex;gap:8px;width:100%">
          <el-input v-model="serverDialog.form.executablePath"
                    placeholder="SCPSL.exe 的完整路径" style="flex:1"></el-input>
          <el-button :loading="serverDialog.discovering" @click="discoverExecutable(false)">自动查找</el-button>
        </div>
        <div v-if="serverDialog.candidates.length > 1" style="margin-top:6px;width:100%">
          <el-select v-model="serverDialog.form.executablePath"
                     placeholder="或从探测到的路径中选择" style="width:100%">
            <el-option v-for="c in serverDialog.candidates" :key="c" :label="c" :value="c"></el-option>
          </el-select>
        </div>
        <span class="muted">{{ serverDialog.hint || '留空可按「自动查找」：优先取本机正在运行的 SCPSL 进程，其次扫 Steam 各库目录' }}</span>
      </el-form-item>

      <el-form-item label="工作目录">
        <el-input v-model="serverDialog.form.workingDirectory"
                  placeholder="留空则用可执行文件所在目录"></el-input>
      </el-form-item>

      <el-form-item label="游戏端口">
        <el-input-number v-model="serverDialog.form.gamePort" :min="1" :max="65535"
                         controls-position="right"></el-input-number>
        <span class="muted" style="margin-left:10px">对应游戏 -port，多台不能重复</span>
      </el-form-item>

      <el-form-item label="额外参数">
        <el-input v-model="serverDialog.form.extraArguments"
                  placeholder="透传给游戏的参数，可留空；含空格的值用双引号包裹"></el-input>
      </el-form-item>

      <el-form-item label="启动与崩溃">
        <div class="perm-list">
          <el-checkbox v-model="serverDialog.form.autoStart">机器人启动时自动拉起</el-checkbox>
          <el-checkbox v-model="serverDialog.form.restartOnCrash">崩溃后自动重启</el-checkbox>
          <el-checkbox v-model="serverDialog.form.enableHeartbeat">静默崩溃检测（心跳）</el-checkbox>
        </div>
      </el-form-item>

      <el-form-item label="重启次数上限">
        <el-input-number v-model="serverDialog.form.restartLimit" :min="0" :max="100"
                         controls-position="right"></el-input-number>
        <span class="muted" style="margin-left:10px">时间窗口内最多自动拉起几次，0 = 不自动重启</span>
      </el-form-item>

      <el-form-item label="控制台显示">
        <el-select v-model="serverDialog.form.consoleLevel" style="width:220px">
          <el-option v-for="o in local.consoleLevels" :key="o.key" :label="o.label" :value="o.key"></el-option>
        </el-select>
        <span class="muted" style="margin-left:10px">「服务器进程」页控制台采集哪些输出</span>
      </el-form-item>

      <el-collapse>
        <el-collapse-item title="高级：心跳阈值 / 缓冲 / 输出" name="adv">
          <el-form label-width="150px">
            <el-form-item label="心跳超时阈值（秒）">
              <el-input-number v-model="serverDialog.form.heartbeatSpanMaxThreshold" :min="5" :max="3600"
                               controls-position="right"></el-input-number>
            </el-form-item>
            <el-form-item label="异常后重启倒计时（秒）">
              <el-input-number v-model="serverDialog.form.heartbeatRestartInSeconds" :min="1" :max="600"
                               controls-position="right"></el-input-number>
            </el-form-item>
            <el-form-item label="限流窗口（秒）">
              <el-input-number v-model="serverDialog.form.restartTimeWindowSeconds" :min="10" :max="86400"
                               controls-position="right"></el-input-number>
            </el-form-item>
            <el-form-item label="优雅停止等待（秒）">
              <el-input-number v-model="serverDialog.form.gracefulStopTimeoutSeconds" :min="1" :max="600"
                               controls-position="right"></el-input-number>
            </el-form-item>
            <el-form-item label="机器人→游戏 缓冲">
              <el-input-number v-model="serverDialog.form.laToSlBufferSize" :min="101" :step="1000"
                               controls-position="right"></el-input-number>
              <span class="muted" style="margin-left:10px">对应 -rxbuffer</span>
            </el-form-item>
            <el-form-item label="游戏→机器人 缓冲">
              <el-input-number v-model="serverDialog.form.slToLaBufferSize" :min="351" :step="10000"
                               controls-position="right"></el-input-number>
              <span class="muted" style="margin-left:10px">对应 -txbuffer</span>
            </el-form-item>
            <el-form-item label="输出选项">
              <div class="perm-list">
                <el-checkbox v-model="serverDialog.form.disableAnsiColors">禁用游戏端 ANSI 颜色</el-checkbox>
                <el-checkbox v-model="serverDialog.form.redirectStandardStreams">把 stdout/stderr 转发到面板</el-checkbox>
              </div>
            </el-form-item>
          </el-form>
        </el-collapse-item>
      </el-collapse>
    </el-form>

    <template #footer>
      <el-button @click="serverDialog.visible = false">取消</el-button>
      <el-button type="primary" :loading="serverDialog.loading" @click="saveServer">保存</el-button>
    </template>
  </el-dialog>

  <!-- ============ 查看配置（对应官方 lacfg） ============ -->
  <el-dialog v-model="localConfigDialog.visible" title="当前配置（对应官方指令 lacfg）" width="760px">
    <div v-if="localConfigDialog.data">
      <el-descriptions :column="2" border size="small">
        <el-descriptions-item label="能力状态">
          {{ localConfigDialog.data.enabled ? '已启用' : '未启用' }}
        </el-descriptions-item>
        <el-descriptions-item label="托管服务器数">{{ localConfigDialog.data.serverCount }}</el-descriptions-item>
        <el-descriptions-item label="配置来源">
          {{ localConfigDialog.data.source === 'config-file' ? '配置文件' : 'appsettings 种子' }}
        </el-descriptions-item>
        <el-descriptions-item label="控制台缓冲行数">
          {{ localConfigDialog.data.global.consoleBufferLines }}
        </el-descriptions-item>
        <el-descriptions-item label="日志目录">{{ localConfigDialog.data.global.logDirectory }}</el-descriptions-item>
        <el-descriptions-item label="写日志文件">
          {{ localConfigDialog.data.global.writeLogFiles ? '是' : '否' }}
        </el-descriptions-item>
        <el-descriptions-item label="日志保留天数">
          {{ localConfigDialog.data.global.logExpirationDays > 0
             ? localConfigDialog.data.global.logExpirationDays + ' 天' : '不清理' }}
        </el-descriptions-item>
        <el-descriptions-item label="默认可执行文件">
          {{ localConfigDialog.data.global.defaultExecutablePath || '（未设置，靠自动探测）' }}
        </el-descriptions-item>
      </el-descriptions>

      <h4 style="margin:16px 0 6px">路径</h4>
      <p class="muted" style="word-break:break-all;margin:0 0 4px">
        服务器定义：{{ localConfigDialog.data.serversConfigPath }}
        （{{ localConfigDialog.data.serversConfigExists ? '已存在' : '尚未生成' }}）
      </p>
      <p class="muted" style="word-break:break-all;margin:0">
        内容根目录：{{ localConfigDialog.data.contentRoot }}
      </p>

      <h4 style="margin:16px 0 6px">托管服务器</h4>
      <el-table :data="localConfigDialog.data.servers" border size="small" max-height="240">
        <el-table-column prop="id" label="Id" width="120"></el-table-column>
        <el-table-column prop="name" label="名称"></el-table-column>
        <el-table-column prop="gamePort" label="端口" width="90"></el-table-column>
        <el-table-column label="控制台级别" width="120">
          <template #default="{ row }">{{ localLevelLabel(row.consoleLevel) }}</template>
        </el-table-column>
        <el-table-column label="自动启动" width="90">
          <template #default="{ row }">{{ row.autoStart ? '是' : '否' }}</template>
        </el-table-column>
      </el-table>
    </div>
    <el-empty v-else description="加载中…"></el-empty>
    <template #footer>
      <el-button @click="localConfigDialog.visible = false">关闭</el-button>
    </template>
  </el-dialog>

  <!-- ============ 内置命令说明（对应官方 help） ============ -->
  <el-dialog v-model="localHelpVisible" title="命令说明（对应官方指令 help）" width="880px">
    <h4 style="margin:0 0 8px">一、LocalAdmin 本地命令</h4>
    <p class="muted" style="margin:0 0 8px">
      官方 <code>help</code> 输出的「LocalAdmin Commands」一节的完整对应关系 —— 已实现的都做成了面板按钮/开关。
    </p>
    <el-table :data="localCommandMap" border size="small">
      <el-table-column prop="command" label="指令" width="125"></el-table-column>
      <el-table-column prop="official" label="官方说明" width="330"></el-table-column>
      <el-table-column prop="panel" label="面板对应"></el-table-column>
      <el-table-column label="状态" width="92" align="center">
        <template #default="{ row }">
          <el-tag size="small" :type="row.implemented ? 'success' : 'info'">
            {{ row.implemented ? '已实现' : '未实现' }}
          </el-tag>
        </template>
      </el-table-column>
    </el-table>
    <p class="muted" style="margin:8px 0 0">
      <b>P（Plugin Manager）未实现</b>的原因：它是「给游戏侧装插件」的工具（拉 GitHub release 写入游戏插件目录、
      记录版本、保存 PAT），与本模块「托管进程」的职责正交，工程量另算；且其插件源来自 <code>PluginAliases</code>
      配置，需要先配好才有意义。详见 <code>LocalAdmin面板实现.md</code> §7.4。
    </p>

    <h4 style="margin:18px 0 8px">二、游戏控制台命令</h4>
    <el-alert type="success" :closable="false" show-icon
              title="游戏自带命令不需要单独实现 —— 面板会把未命中本地命令的输入原样透传给游戏执行"></el-alert>
    <p class="muted" style="margin:8px 0 0">
      官方 <code>help</code> 输出的「Game Commands」一节属于<b>游戏与插件</b>，不是 LocalAdmin 的能力。
      包括游戏原生命令（<code>roundrestart</code>、<code>players</code>、<code>srvcfg</code>、<code>reload</code>、
      <code>labapi</code>、<code>forcestart</code> …），以及已装插件注册的命令
      （LabAPI/EXILED 的 <code>pluginmanager</code>、<code>customroles</code>/<code>customitems</code>，以及你自己插件里的那些）。
    </p>
    <p class="muted" style="margin:8px 0 12px">
      因为这份清单会随<b>已装插件变化</b>，硬编码到面板里必然会过时。要看当前真实清单，
      让游戏自己打印即可：在命令框执行 <code>help</code>，输出会出现在下方终端里。
    </p>
    <el-button type="primary" plain :disabled="!localRunning" @click="runGameHelp">
      在游戏控制台执行 help
    </el-button>
    <span class="muted" style="margin-left:10px" v-if="!localRunning">（服务器未运行，无法执行）</span>

    <template #footer>
      <el-button @click="localHelpVisible = false">关闭</el-button>
    </template>
  </el-dialog>

  <!-- ============ 许可与致谢（对应官方 license） ============ -->
  <el-dialog v-model="localLicenseVisible" title="许可与致谢（对应官方指令 license）" width="720px">
    <p style="margin-top:0">
      本功能复刻自 Northwood Studios 开源的
      <a href="https://github.com/northwood-studios/LocalAdmin-V2" target="_blank" rel="noreferrer">LocalAdmin-V2</a>，
      包括其控制台 TCP 协议与本地命令集。
    </p>
    <el-alert type="info" :closable="false" show-icon
              title="LocalAdmin-V2 采用 MIT 许可（Copyright by Łukasz &quot;zabszk&quot; Jurczyk and KernelError, 2019 - 2026）"
              style="margin-bottom:10px"></el-alert>
    <pre class="license-block">Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.</pre>
    <p class="muted">
      另：LocalAdmin 内含 Utf8Json（Yoshifumi Kawai，MIT 许可）；本面板前端使用 Vue 3 与 Element Plus。
    </p>
    <template #footer>
      <el-button @click="localLicenseVisible = false">关闭</el-button>
    </template>
  </el-dialog>

  <!-- ==================== 玩家数据编辑 / 新建 ==================== -->
  <el-dialog v-model="playerDialog.visible" :title="playerDialog.isEdit ? '编辑玩家数据' : '新建玩家记录'" width="560px">
    <el-form label-width="100px">
      <el-form-item label="玩家 ID">
        <el-input v-model="playerDialog.form.id" :disabled="playerDialog.isEdit" placeholder="Steam64 ID 或游戏内唯一标识"></el-input>
      </el-form-item>
      <el-form-item label="玩家昵称">
        <el-input v-model="playerDialog.form.playerName" placeholder="游戏内昵称"></el-input>
      </el-form-item>
      <el-form-item label="绑定 QQ">
        <el-input v-model.number="playerDialog.form.qqId" placeholder="QQ 号码（选填）"></el-input>
      </el-form-item>
      <el-form-item label="管理员身份">
        <el-switch v-model="playerDialog.form.isAdmin"></el-switch>
      </el-form-item>
      <el-form-item label="管理组备注" v-if="playerDialog.form.isAdmin">
        <el-input v-model="playerDialog.form.adminNote" placeholder="例如 owner / admin"></el-input>
      </el-form-item>
      <el-form-item label="游玩时长(秒)">
        <el-input-number v-model="playerDialog.form.playTimeSeconds" :min="0" controls-position="right"></el-input-number>
        <span class="muted" style="margin-left:8px">约 {{ formatPlayTime(playerDialog.form.playTimeSeconds) }}</span>
      </el-form-item>
      <el-form-item label="击杀玩家数">
        <el-input-number v-model="playerDialog.form.playersKilled" :min="0" controls-position="right"></el-input-number>
      </el-form-item>
      <el-form-item label="击杀 SCP 数">
        <el-input-number v-model="playerDialog.form.scpsKilled" :min="0" controls-position="right"></el-input-number>
      </el-form-item>
      <el-form-item label="死亡次数">
        <el-input-number v-model="playerDialog.form.deaths" :min="0" controls-position="right"></el-input-number>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="playerDialog.visible = false">取消</el-button>
      <el-button type="primary" :loading="playerDialog.saving" @click="savePlayer">保存</el-button>
    </template>
  </el-dialog>

  <!-- ==================== 玩家排行榜 ==================== -->
  <el-dialog v-model="rankingDialog.visible" title="玩家数据排行榜" width="680px">
    <div class="toolbar">
      <el-radio-group v-model="rankingDialog.metric" @change="loadRankings">
        <el-radio-button label="playtime">游玩时长</el-radio-button>
        <el-radio-button label="kills">击杀玩家</el-radio-button>
        <el-radio-button label="scps">击杀 SCP</el-radio-button>
        <el-radio-button label="deaths">死亡次数</el-radio-button>
      </el-radio-group>
      <el-button :loading="rankingDialog.loading" @click="loadRankings" style="margin-left:auto">刷新</el-button>
    </div>
    <el-table :data="rankingDialog.items" border stripe v-loading="rankingDialog.loading" max-height="400">
      <el-table-column label="排名" width="70" align="center">
        <template #default="{ $index }">
          <el-tag size="small" :type="$index === 0 ? 'danger' : ($index === 1 ? 'warning' : ($index === 2 ? 'success' : 'info'))">
            #{{ $index + 1 }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="playerName" label="玩家昵称" min-width="140"></el-table-column>
      <el-table-column prop="id" label="ID" min-width="160" show-overflow-tooltip></el-table-column>
      <el-table-column label="数值" width="130" align="right">
        <template #default="{ row }">
          <b v-if="rankingDialog.metric === 'playtime'">{{ formatPlayTime(row.value) }}</b>
          <b v-else>{{ row.value }}</b>
        </template>
      </el-table-column>
      <el-table-column label="QQ" width="120">
        <template #default="{ row }">
          {{ row.qqId && row.qqId > 0 ? row.qqId : '-' }}
        </template>
      </el-table-column>
    </el-table>
    <template #footer>
      <el-button @click="rankingDialog.visible = false">关闭</el-button>
    </template>
  </el-dialog>

  <!-- ==================== 封禁编辑 / 新建 ==================== -->
  <el-dialog v-model="banDialog.visible" :title="banDialog.isEdit ? '编辑封禁记录' : '新增封禁记录'" width="560px">
    <el-form label-width="100px">
      <el-form-item label="目标 ID">
        <el-input v-model="banDialog.form.id" :disabled="banDialog.isEdit" placeholder="Steam64 ID 或游戏内唯一标识"></el-input>
      </el-form-item>
      <el-form-item label="目标 IP">
        <el-input v-model="banDialog.form.playerIP" placeholder="0.0.0.0"></el-input>
      </el-form-item>
      <el-form-item label="封禁时长">
        <el-select v-model="banDialog.durationPreset" placeholder="选择预设或自定义" style="width:100%" @change="onBanPresetChange">
          <el-option label="永久封禁" :value="-1"></el-option>
          <el-option label="10 分钟" :value="600"></el-option>
          <el-option label="1 小时" :value="3600"></el-option>
          <el-option label="1 天" :value="86400"></el-option>
          <el-option label="7 天" :value="604800"></el-option>
          <el-option label="30 天" :value="2592000"></el-option>
          <el-option label="自定义解封时间" :value="0"></el-option>
        </el-select>
      </el-form-item>
      <el-form-item label="解封时间" v-if="banDialog.durationPreset === 0">
        <el-date-picker v-model="banDialog.form.unbanTime" type="datetime" placeholder="选择解封具体时间" style="width:100%"></el-date-picker>
      </el-form-item>
      <el-form-item label="管理员 ID">
        <el-input v-model="banDialog.form.adminId" placeholder="执行管理员 ID"></el-input>
      </el-form-item>
      <el-form-item label="管理员名称">
        <el-input v-model="banDialog.form.adminName" placeholder="执行管理员昵称"></el-input>
      </el-form-item>
      <el-form-item label="封禁原因">
        <el-input v-model="banDialog.form.reason" type="textarea" :rows="3" placeholder="填写封禁原因"></el-input>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="banDialog.visible = false">取消</el-button>
      <el-button type="primary" :loading="banDialog.saving" @click="saveBan">保存</el-button>
    </template>
  </el-dialog>
  </div>
`,
  setup() {
    const serversModule = useServers();
    const accountsModule = useAccounts();
    const localAdminModule = useLocalAdmin();
    const databaseModule = useDatabase();
    const botModule = useBot();

    return {
      ...serversModule,
      ...accountsModule,
      ...localAdminModule,
      ...databaseModule,
      ...botModule,
      can,
      fmtTime,
      meta,
      randomPassword,
    };
  }
};
