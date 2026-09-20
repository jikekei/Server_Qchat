import { reactive, ref } from '../deps.js';
import { api, token, session, loadMeta, TOKEN_KEY } from '../api.js';
import { useOverview } from '../modules/useOverview.js';
import { useServers } from '../modules/useServers.js';
import { ElMessage } from '../deps.js';

export const LoginView = {
  name: 'LoginView',
  template: `
  <div v-if="!session" class="login-wrap">
    <el-card class="login-card">
      <div class="login-brand">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
          <path d="M3 15 Q12 5 21 15"/>
          <line x1="2" y1="15" x2="22" y2="15"/>
          <line x1="8" y1="11" x2="8" y2="15"/>
          <line x1="16" y1="11" x2="16" y2="15"/>
          <line x1="4.5" y1="15" x2="4.5" y2="19"/>
          <line x1="19.5" y1="15" x2="19.5" y2="19"/>
        </svg>
        <h2>Qridge</h2>
      </div>
      <p class="login-sub">SCP: Secret Laboratory 服务器管理</p>
      <el-form @submit.prevent>
        <el-form-item>
          <el-input v-model="loginForm.username" placeholder="用户名" size="large" @keyup.enter="doLogin"></el-input>
        </el-form-item>
        <el-form-item>
          <el-input v-model="loginForm.password" type="password" placeholder="密码" size="large" show-password @keyup.enter="doLogin"></el-input>
        </el-form-item>
        <el-button type="primary" size="large" style="width:100%" :loading="logging" @click="doLogin">登 录</el-button>
      </el-form>
      <p class="hint">
        默认账号见程序启动日志。<br />
        密码每次启动随机生成。
      </p>
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
        localStorage.setItem(TOKEN_KEY, data.token);
        loginForm.password = '';

        await loadMeta();
        await loadOverview();
        await loadServers();
        startOverviewPolling();
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
    };
  }
};
