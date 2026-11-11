import { computed } from '../deps.js';
import { session, page, can, theme, toggleTheme, api, clearSession, mobileMenuOpen } from '../api.js';
import { useAccounts } from '../modules/useAccounts.js';
import { useLocalAdmin } from '../modules/useLocalAdmin.js';
import { ElMessage } from '../deps.js';

const PAGE_META = {
  'overview':   { title: '服务器总览' },
  'servers':    { title: '在线服务器' },
  'local':      { title: '服务器进程 (LocalAdmin)' },
  'bot':        { title: 'QQ 机器人' },
  'game-admin': { title: '游戏管理员' },
  'database':   { title: '数据库管理' },
  'accounts':   { title: '账号管理' },
  'audit':      { title: '审计日志' },
  'logging':    { title: '日志级别' },
  'operations': { title: 'API 与日志归档' },
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
        </div>
      </div>

      <div class="user">
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

    const currentMeta = computed(() => PAGE_META[page.value] || { title: '控制台' });

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
      currentMeta,
    };
  }
};
