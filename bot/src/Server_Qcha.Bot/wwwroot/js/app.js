import { createApp } from './deps.js';
import { session, page, loadMeta, api, clearSession } from './api.js';
import { useOverview } from './modules/useOverview.js';
import { useServers } from './modules/useServers.js';

import { LoginView } from './views/LoginView.js';
import { AppHeader } from './views/AppHeader.js';
import { AppSidebar } from './views/AppSidebar.js';
import { OverviewView } from './views/OverviewView.js';
import { ServersView } from './views/ServersView.js';
import { LocalAdminView } from './views/LocalAdminView.js';
import { BotView } from './views/BotView.js';
import { DatabaseView } from './views/DatabaseView.js';
import { AccountsView } from './views/AccountsView.js';
import { AuditView } from './views/AuditView.js';
import { LoggingView } from './views/LoggingView.js';
import { AppDialogs } from './views/AppDialogs.js';

const app = createApp({
  setup() {
    const { loadOverview, startOverviewPolling } = useOverview();
    const { loadServers } = useServers();

    // 启动恢复会话
    (async function bootstrap() {
      try { await loadMeta(); } catch (e) { /* 忽略 */ }
      if (!localStorage.getItem('scpsl_panel_token')) return;
      try {
        const me = await api('/auth/me');
        session.value = me;
        await loadOverview();
        await loadServers();
        startOverviewPolling();
      } catch (e) {
        clearSession();
      }
    })();

    return {
      session,
      page,
    };
  }
});

app.component('login-view', LoginView);
app.component('app-header', AppHeader);
app.component('app-sidebar', AppSidebar);
app.component('overview-view', OverviewView);
app.component('servers-view', ServersView);
app.component('local-admin-view', LocalAdminView);
app.component('bot-view', BotView);
app.component('database-view', DatabaseView);
app.component('accounts-view', AccountsView);
app.component('audit-view', AuditView);
app.component('logging-view', LoggingView);
app.component('app-dialogs', AppDialogs);

app.use(window.ElementPlus).mount('#app');
