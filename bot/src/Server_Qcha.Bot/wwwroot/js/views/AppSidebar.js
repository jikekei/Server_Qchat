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
      <el-aside class="aside" width="218px">
        <div class="sidebar-nav-wrap">
          <el-menu :default-active="page" @select="onMenuSelect">
            <template v-if="can('servers.view') || can('server.control')">
              <div class="nav-group-label">监控与运行</div>
              <el-menu-item v-if="can('servers.view')" index="overview">
                <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="3" y="3" width="7" height="9" rx="1"></rect>
                  <rect x="14" y="3" width="7" height="5" rx="1"></rect>
                  <rect x="14" y="12" width="7" height="9" rx="1"></rect>
                  <rect x="3" y="16" width="7" height="5" rx="1"></rect>
                </svg>
                <span>服务器总览</span>
              </el-menu-item>

              <el-menu-item v-if="can('servers.view')" index="servers">
                <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="2" y="2" width="20" height="8" rx="2" ry="2"></rect>
                  <rect x="2" y="14" width="20" height="8" rx="2" ry="2"></rect>
                  <line x1="6" y1="6" x2="6.01" y2="6"></line>
                  <line x1="6" y1="18" x2="6.01" y2="18"></line>
                </svg>
                <span>在线服务器</span>
              </el-menu-item>

              <el-menu-item v-if="can('server.control')" index="local">
                <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <polyline points="4 17 10 11 4 5"></polyline>
                  <line x1="12" y1="19" x2="20" y2="19"></line>
                </svg>
                <span>服务器进程</span>
              </el-menu-item>
            </template>

            <template v-if="can('bot.manage') || can('game-admin.manage') || can('accounts.manage')">
              <div class="nav-group-label">社区与权限</div>
              <el-menu-item v-if="can('bot.manage')" index="bot">
                <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="3" y="11" width="18" height="10" rx="2"></rect>
                  <circle cx="12" cy="5" r="2"></circle>
                  <path d="M12 7v4"></path>
                  <line x1="8" y1="16" x2="8.01" y2="16"></line>
                  <line x1="16" y1="16" x2="16.01" y2="16"></line>
                </svg>
                <span>QQ 机器人</span>
              </el-menu-item>

              <el-menu-item v-if="can('game-admin.manage')" index="game-admin">
                <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path>
                </svg>
                <span>游戏管理员</span>
              </el-menu-item>

              <el-menu-item v-if="can('accounts.manage')" index="accounts">
                <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path>
                  <circle cx="9" cy="7" r="4"></circle>
                  <path d="M23 21v-2a4 4 0 0 0-3-3.87"></path>
                  <path d="M16 3.13a4 4 0 0 1 0 7.75"></path>
                </svg>
                <span>账号管理</span>
              </el-menu-item>
            </template>

            <template v-if="can('database.manage') || can('audit.view') || can('logging.manage') || can('accounts.manage')">
              <div class="nav-group-label">数据与追溯</div>
              <el-menu-item v-if="can('database.manage')" index="database">
                <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <ellipse cx="12" cy="5" rx="9" ry="3"></ellipse>
                  <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"></path>
                  <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"></path>
                </svg>
                <span>数据库管理</span>
              </el-menu-item>

              <el-menu-item v-if="can('audit.view')" index="audit">
                <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                  <polyline points="14 2 14 8 20 8"></polyline>
                  <line x1="16" y1="13" x2="8" y2="13"></line>
                  <line x1="16" y1="17" x2="8" y2="17"></line>
                  <polyline points="10 9 9 9 8 9"></polyline>
                </svg>
                <span>审计日志</span>
              </el-menu-item>

              <el-menu-item v-if="can('logging.manage')" index="logging">
                <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <line x1="4" y1="21" x2="4" y2="14"></line>
                  <line x1="4" y1="10" x2="4" y2="3"></line>
                  <line x1="12" y1="21" x2="12" y2="12"></line>
                  <line x1="12" y1="8" x2="12" y2="3"></line>
                  <line x1="20" y1="21" x2="20" y2="16"></line>
                  <line x1="20" y1="12" x2="20" y2="3"></line>
                  <line x1="1" y1="14" x2="7" y2="14"></line>
                  <line x1="9" y1="8" x2="15" y2="8"></line>
                  <line x1="17" y1="16" x2="23" y2="16"></line>
                </svg>
                <span>日志级别</span>
              </el-menu-item>

              <el-menu-item v-if="can('accounts.manage')" index="operations">
                <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <polyline points="16 18 22 12 16 6"></polyline>
                  <polyline points="8 6 2 12 8 18"></polyline>
                </svg>
                <span>API 与日志归档</span>
              </el-menu-item>
            </template>
          </el-menu>
        </div>

      </el-aside>

      <!-- 移动端侧滑抽屉导航 (Drawer) -->
      <el-drawer
        v-model="mobileMenuOpen"
        direction="ltr"
        size="280px"
        :with-header="false"
        class="mobile-drawer"
        append-to-body>
        <div class="mobile-drawer-content">
          <div class="mobile-drawer-header">
            <div class="brand">
              <div class="brand-icon-wrap">
                <svg class="brand-logo" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
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
            </div>
          </div>

          <div class="mobile-drawer-menu">
            <el-menu :default-active="page" @select="onMenuSelect">
              <div class="nav-group-label" v-if="can('servers.view') || can('server.control')">监控与运行</div>
              <el-menu-item v-if="can('servers.view')" index="overview">
                <span class="menu-text">服务器总览</span>
              </el-menu-item>
              <el-menu-item v-if="can('servers.view')" index="servers">
                <span class="menu-text">在线服务器</span>
              </el-menu-item>
              <el-menu-item v-if="can('server.control')" index="local">
                <span class="menu-text">服务器进程</span>
              </el-menu-item>

              <div class="nav-group-label" v-if="can('bot.manage') || can('game-admin.manage') || can('accounts.manage')">社区与权限</div>
              <el-menu-item v-if="can('bot.manage')" index="bot">
                <span class="menu-text">QQ 机器人</span>
              </el-menu-item>
              <el-menu-item v-if="can('game-admin.manage')" index="game-admin">
                <span class="menu-text">游戏管理员</span>
              </el-menu-item>
              <el-menu-item v-if="can('accounts.manage')" index="accounts">
                <span class="menu-text">账号管理</span>
              </el-menu-item>

              <div class="nav-group-label" v-if="can('database.manage') || can('audit.view') || can('logging.manage') || can('accounts.manage')">数据与追溯</div>
              <el-menu-item v-if="can('database.manage')" index="database">
                <span class="menu-text">数据库管理</span>
              </el-menu-item>
              <el-menu-item v-if="can('audit.view')" index="audit">
                <span class="menu-text">审计日志</span>
              </el-menu-item>
              <el-menu-item v-if="can('logging.manage')" index="logging">
                <span class="menu-text">日志级别</span>
              </el-menu-item>
              <el-menu-item v-if="can('accounts.manage')" index="operations">
                <span class="menu-text">API 与日志归档</span>
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
