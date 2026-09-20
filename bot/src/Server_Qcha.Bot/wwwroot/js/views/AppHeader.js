import { session, theme, toggleTheme, api, clearSession, mobileMenuOpen } from '../api.js';
import { useAccounts } from '../modules/useAccounts.js';
import { useLocalAdmin } from '../modules/useLocalAdmin.js';
import { ElMessage } from '../deps.js';

export const AppHeader = {
  name: 'AppHeader',
  template: `
    <el-header class="header" height="56px">
      <div class="header-left">
        <button class="mobile-menu-btn" @click="mobileMenuOpen = !mobileMenuOpen" aria-label="打开菜单">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <line x1="3" y1="6" x2="21" y2="6"/>
            <line x1="3" y1="12" x2="21" y2="12"/>
            <line x1="3" y1="18" x2="21" y2="18"/>
          </svg>
        </button>
        <div class="brand">
          <svg class="brand-logo" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
            <path d="M3 15 Q12 5 21 15"/>
            <line x1="2" y1="15" x2="22" y2="15"/>
            <line x1="8" y1="11" x2="8" y2="15"/>
            <line x1="16" y1="11" x2="16" y2="15"/>
            <line x1="4.5" y1="15" x2="4.5" y2="19"/>
            <line x1="19.5" y1="15" x2="19.5" y2="19"/>
          </svg>
          Qridge
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
        <el-tag size="small" effect="dark" type="info">{{ session.displayName || session.username }}</el-tag>
        <div class="desktop-user-actions">
          <el-button link @click="passwordDialog.visible = true">修改密码</el-button>
          <el-button link type="danger" @click="doLogout">退出登录</el-button>
        </div>
      </div>
    </el-header>
`,
  setup() {
    const { passwordDialog } = useAccounts();
    const { stopLocalPolling } = useLocalAdmin();

    async function doLogout() {
      try { await api('/auth/logout', { method: 'POST' }); } catch (e) { /* 忽略 */ }
      stopLocalPolling();
      clearSession();
      ElMessage.success('已退出登录');
    }

    return {
      session,
      theme,
      toggleTheme,
      passwordDialog,
      doLogout,
      mobileMenuOpen,
    };
  }
};
