import { page, can, capabilities, mobileMenuOpen, session, api, clearSession } from '../api.js';
import { useOverview } from '../modules/useOverview.js';
import { useServers } from '../modules/useServers.js';
import { useLocalAdmin } from '../modules/useLocalAdmin.js';
import { useBot } from '../modules/useBot.js';
import { useDatabase } from '../modules/useDatabase.js';
import { useAccounts } from '../modules/useAccounts.js';
import { useAudit } from '../modules/useAudit.js';
import { ElMessage } from '../deps.js';

export const AppSidebar = {
  name: 'AppSidebar',
  template: `
    <div>
      <!-- 桌面端固定侧边栏 -->
      <el-aside class="aside" width="170px">
        <el-menu :default-active="page" @select="onMenuSelect">
          <el-menu-item index="overview">服务器总览</el-menu-item>
          <el-menu-item index="servers">服务器</el-menu-item>
          <el-menu-item v-if="can('server.control')" index="local">服务器进程</el-menu-item>
          <el-menu-item v-if="can('bot.manage')" index="bot">QQ 机器人</el-menu-item>
          <el-menu-item v-if="can('database.manage')" index="database">数据库管理</el-menu-item>
          <el-menu-item v-if="can('logging.manage')" index="logging">日志级别</el-menu-item>
          <el-menu-item v-if="can('accounts.manage')" index="accounts">账号管理</el-menu-item>
          <el-menu-item v-if="can('audit.view')" index="audit">审计日志</el-menu-item>
        </el-menu>
      </el-aside>

      <!-- 移动端侧滑抽屉导航 (Drawer) -->
      <el-drawer
        v-model="mobileMenuOpen"
        direction="ltr"
        size="270px"
        :with-header="false"
        class="mobile-drawer"
        append-to-body>
        <div class="mobile-drawer-content">
          <div class="mobile-drawer-header">
            <div class="brand">
              <svg class="brand-logo" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                <path d="M3 15 Q12 5 21 15"/>
                <line x1="2" y1="15" x2="22" y2="15"/>
                <line x1="8" y1="11" x2="8" y2="15"/>
                <line x1="16" y1="11" x2="16" y2="15"/>
                <line x1="4.5" y1="15" x2="4.5" y2="19"/>
                <line x1="19.5" y1="15" x2="19.5" y2="19"/>
              </svg>
              <span>Qridge</span>
            </div>
            <button class="mobile-drawer-close" @click="mobileMenuOpen = false" aria-label="关闭">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
                <line x1="18" y1="6" x2="6" y2="18"/>
                <line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            </button>
          </div>

          <div class="mobile-drawer-user" v-if="session">
            <div class="mobile-user-avatar">
              {{ (session.displayName || session.username || 'A')[0].toUpperCase() }}
            </div>
            <div class="mobile-user-info">
              <div class="mobile-user-name">{{ session.displayName || session.username }}</div>
              <div class="mobile-user-role muted">{{ session.username === 'admin' ? '系统管理员' : '面板用户' }}</div>
            </div>
          </div>

          <div class="mobile-drawer-menu">
            <el-menu :default-active="page" @select="onMenuSelect">
              <el-menu-item index="overview">
                <span class="menu-text">服务器总览</span>
              </el-menu-item>
              <el-menu-item index="servers">
                <span class="menu-text">服务器</span>
              </el-menu-item>
              <el-menu-item v-if="can('server.control')" index="local">
                <span class="menu-text">服务器进程</span>
              </el-menu-item>
              <el-menu-item v-if="can('bot.manage')" index="bot">
                <span class="menu-text">QQ 机器人</span>
              </el-menu-item>
              <el-menu-item v-if="can('database.manage')" index="database">
                <span class="menu-text">数据库管理</span>
              </el-menu-item>
              <el-menu-item v-if="can('logging.manage')" index="logging">
                <span class="menu-text">日志级别</span>
              </el-menu-item>
              <el-menu-item v-if="can('accounts.manage')" index="accounts">
                <span class="menu-text">账号管理</span>
              </el-menu-item>
              <el-menu-item v-if="can('audit.view')" index="audit">
                <span class="menu-text">审计日志</span>
              </el-menu-item>
            </el-menu>
          </div>

          <div class="mobile-drawer-footer">
            <el-button @click="onMobilePassword">修改密码</el-button>
            <el-button type="danger" plain @click="onMobileLogout">退出登录</el-button>
          </div>
        </div>
      </el-drawer>
    </div>
`,
  setup() {
    const { loadOverview } = useOverview();
    const { loadServers } = useServers();
    const { startLocalPolling, stopLocalPolling, loadLogSettings } = useLocalAdmin();
    const { loadBotStatus } = useBot();
    const { db, onDbTabChange } = useDatabase();
    const { loadAccounts, passwordDialog } = useAccounts();
    const { loadAudit } = useAudit();

    function onMenuSelect(index) {
      page.value = index;
      mobileMenuOpen.value = false;
      stopLocalPolling();
      if (index === 'overview') loadOverview();
      else if (index === 'servers') loadServers();
      else if (index === 'accounts') loadAccounts();
      else if (index === 'audit') loadAudit();
      else if (index === 'local' && capabilities.localAdmin) startLocalPolling();
      else if (index === 'logging') loadLogSettings();
      else if (index === 'bot') loadBotStatus();
      else if (index === 'database') onDbTabChange(db.activeTab);
    }

    function onMobilePassword() {
      mobileMenuOpen.value = false;
      passwordDialog.visible = true;
    }

    async function onMobileLogout() {
      mobileMenuOpen.value = false;
      try { await api('/auth/logout', { method: 'POST' }); } catch (e) { /* 忽略 */ }
      stopLocalPolling();
      clearSession();
      ElMessage.success('已退出登录');
    }

    return {
      page,
      can,
      session,
      mobileMenuOpen,
      onMenuSelect,
      onMobilePassword,
      onMobileLogout,
    };
  }
};
