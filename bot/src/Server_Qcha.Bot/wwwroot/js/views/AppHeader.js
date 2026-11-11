import { computed } from '../deps.js';
import { session, page, can, theme, toggleTheme, api, clearSession, mobileMenuOpen } from '../api.js';
import { useAccounts } from '../modules/useAccounts.js';
import { useLocalAdmin } from '../modules/useLocalAdmin.js';
import { useOverview } from '../modules/useOverview.js';
import { ElMessage } from '../deps.js';

const PAGE_META = {
  'overview':   { title: '服务器总览', desc: '全服实时玩家趋势、服务器负载矩阵与主机资源健康监控' },
  'servers':    { title: '在线服务器', desc: '多服实时在线状态、玩家列表管控、全服广播与回合控制' },
  'local':      { title: '服务器进程 (LocalAdmin)', desc: '独立守护进程监控、SCPSL 实例生命周期、SteamCMD 更新与实时控制台' },
  'bot':        { title: 'QQ 机器人', desc: 'NapCat (OneBot 11) 与 QQ 官方开放平台双引擎热切换及群聊联动' },
  'game-admin': { title: '游戏管理员', desc: '可视化接管 config_remoteadmin.txt 管理员、权限组、插件节点与个人覆盖' },
  'database':   { title: '数据库管理', desc: 'MySQL 玩家战绩统计、封禁记录管理、排行榜与数据表结构维护' },
  'accounts':   { title: '账号管理', desc: '面板多管理员账户、角色预设与细粒度 RBAC 权限授权' },
  'audit':      { title: '审计日志', desc: '全量记录面板与 API 管理指令的操作人、时间戳与执行结果' },
  'logging':    { title: '日志级别', desc: '动态热切换系统与各子模块日志输出级别，立即生效免重启' },
  'operations': { title: 'API 与日志归档', desc: '自动化运维 Bearer 令牌管理与全量服务器历史日志归档检索' },
};

export const AppHeader = {
  name: 'AppHeader',
  template: `
    <el-header class="header" height="58px">
      <div class="header-left">
        <button class="mobile-menu-btn" @click="mobileMenuOpen = !mobileMenuOpen" aria-label="打开菜单">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <line x1="3" y1="6" x2="21" y2="6"/>
            <line x1="3" y1="12" x2="21" y2="12"/>
            <line x1="3" y1="18" x2="21" y2="18"/>
          </svg>
        </button>

        <div class="brand" style="cursor:pointer" @click="can('servers.view') && (page = 'overview')">
          <div class="brand-icon-wrap">
            <svg class="brand-logo" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">
              <path d="M3 15 Q12 5 21 15"/>
              <line x1="2" y1="15" x2="22" y2="15"/>
              <line x1="8" y1="11" x2="8" y2="15"/>
              <line x1="16" y1="11" x2="16" y2="15"/>
              <line x1="4.5" y1="15" x2="4.5" y2="19"/>
              <line x1="19.5" y1="15" x2="19.5" y2="19"/>
            </svg>
          </div>
          <span>Qridge</span>
          <span class="brand-ver">v2.2.0</span>
        </div>

        <div class="header-divider"></div>

        <div class="header-breadcrumb">
          <span class="header-page-title">{{ currentMeta.title }}</span>
          <span class="header-page-desc">· {{ currentMeta.desc }}</span>
        </div>
      </div>

      <div class="user">
        <div v-if="can('servers.view')" class="header-status-pills">
          <div class="header-pill" title="点击查看服务器总览" @click="page = 'overview'">
            <span class="header-pill-dot" :class="{ offline: overview.stats.onlineServers === 0 && overview.stats.localRunningCount === 0 }"></span>
            <span>玩家 {{ overview.stats.totalOnline }}/{{ overview.stats.totalMax }}</span>
          </div>
          <div v-if="can('bot.manage')" class="header-pill" title="点击管理 QQ 机器人" @click="page = 'bot'">
            <span class="header-pill-dot" :class="{ offline: !overview.stats.botConnected }"></span>
            <span>{{ overview.stats.botConnected ? 'Bot 在线' : 'Bot 离线' }}</span>
          </div>
        </div>

        <button class="theme-btn" @click="toggleTheme"
                :title="theme === 'dark' ? '切换到亮色主题' : '切换到暗色主题'">
          <svg v-if="theme === 'dark'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round">
            <circle cx="12" cy="12" r="4"/>
            <line x1="12" y1="2" x2="12" y2="4.5"/><line x1="12" y1="19.5" x2="12" y2="22"/>
            <line x1="2" y1="12" x2="4.5" y2="12"/><line x1="19.5" y1="12" x2="22" y2="12"/>
            <line x1="4.6" y1="4.6" x2="6.4" y2="6.4"/><line x1="17.6" y1="17.6" x2="19.4" y2="19.4"/>
            <line x1="4.6" y1="19.4" x2="6.4" y2="17.6"/><line x1="17.6" y1="6.4" x2="19.4" y2="4.6"/>
          </svg>
          <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
            <path d="M20 13.5A8.5 8.5 0 0 1 10.5 4a7 7 0 1 0 9.5 9.5z"/>
          </svg>
        </button>

        <div class="user-profile-pill">
          <span class="user-avatar-mini">{{ (session.displayName || session.username || 'A')[0].toUpperCase() }}</span>
          <span>{{ session.displayName || session.username }}</span>
        </div>

        <div class="desktop-user-actions">
          <button class="header-action-btn" @click="passwordDialog.visible = true">修改密码</button>
          <button class="header-action-btn danger" @click="doLogout">退出</button>
        </div>
      </div>
    </el-header>
`,
  setup() {
    const { passwordDialog } = useAccounts();
    const { stopLocalPolling } = useLocalAdmin();
    const { overview } = useOverview();

    const currentMeta = computed(() => PAGE_META[page.value] || { title: '控制台', desc: '' });

    async function doLogout() {
      try { await api('/auth/logout', { method: 'POST' }); } catch (e) { /* 忽略 */ }
      stopLocalPolling();
      clearSession();
      ElMessage.success('已退出登录');
    }

    return {
      session,
      page,
      can,
      theme,
      toggleTheme,
      passwordDialog,
      doLogout,
      mobileMenuOpen,
      overview,
      currentMeta,
    };
  }
};
