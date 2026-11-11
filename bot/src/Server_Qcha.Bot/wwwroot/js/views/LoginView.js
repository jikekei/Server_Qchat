import { reactive, ref } from '../deps.js';
import { api, token, session, page, can, loadMeta, TOKEN_KEY, theme, toggleTheme } from '../api.js';
import { useOverview } from '../modules/useOverview.js';
import { useServers } from '../modules/useServers.js';
import { ElMessage } from '../deps.js';

export const LoginView = {
  name: 'LoginView',
  template: `
  <div v-if="!session" class="login-wrap">
    <div style="position:fixed;top:20px;right:24px;z-index:10">
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
    </div>

    <el-card class="login-card">
      <div class="login-brand-row">
        <div class="login-brand">
          <div class="login-logo-box">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">
              <path d="M3 15 Q12 5 21 15"/>
              <line x1="2" y1="15" x2="22" y2="15"/>
              <line x1="8" y1="11" x2="8" y2="15"/>
              <line x1="16" y1="11" x2="16" y2="15"/>
              <line x1="4.5" y1="15" x2="4.5" y2="19"/>
              <line x1="19.5" y1="15" x2="19.5" y2="19"/>
            </svg>
          </div>
          <div class="login-brand-text">
            <h2>Qridge Console</h2>
          </div>
        </div>
      </div>

      <el-form @submit.prevent>
        <el-form-item>
          <el-input v-model="loginForm.username" placeholder="请输入管理员账号" size="large" @keyup.enter="doLogin"></el-input>
        </el-form-item>
        <el-form-item>
          <el-input v-model="loginForm.password" type="password" placeholder="请输入登录密码" size="large" show-password @keyup.enter="doLogin"></el-input>
        </el-form-item>
        <el-button type="primary" size="large" class="login-submit-btn" :loading="logging" @click="doLogin">
          进入运维控制台
        </el-button>
      </el-form>

      <p class="login-footer-hint">初始登录凭据见服务端启动日志。</p>
    </el-card>
  </div>
`,
  setup() {
    const loginForm = reactive({ username: '', password: '' });
    const logging = ref(false);
    const { loadOverview, startOverviewPolling } = useOverview();
    const { loadServers } = useServers();

    async function doLogin() {
      if (!loginForm.username || !loginForm.password) {
        ElMessage.warning('请输入用户名和密码');
        return;
      }
      logging.value = true;
      try {
        const res = await fetch('/api/auth/login', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ username: loginForm.username, password: loginForm.password })
        });
        const text = await res.text();
        const data = text ? JSON.parse(text) : {};
        if (!res.ok) throw new Error(data.error || '登录失败');

        token.value = data.token;
        session.value = data;
        try { sessionStorage.setItem(TOKEN_KEY, data.token); } catch (e) { /* 忽略 */ }
        try { localStorage.removeItem(TOKEN_KEY); } catch (e) { /* 忽略 */ }
        loginForm.password = '';

        await loadMeta();
        if (can('servers.view')) {
          page.value = 'overview';
          await loadOverview();
          await loadServers();
          startOverviewPolling();
        } else if (can('game-admin.manage')) page.value = 'game-admin';
        ElMessage.success('欢迎回来，' + (data.displayName || data.username));
      } catch (e) {
        ElMessage.error(e.message);
      } finally {
        logging.value = false;
      }
    }

    return {
      session,
      loginForm,
      logging,
      doLogin,
      theme,
      toggleTheme,
    };
  }
};
