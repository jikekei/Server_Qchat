import { ref, reactive } from './deps.js';

export const TOKEN_KEY = 'scpsl_panel_token';

export const token = ref(localStorage.getItem(TOKEN_KEY) || '');
export const session = ref(null);
export const page = ref('overview');
export const mobileMenuOpen = ref(false);

const THEME_KEY = 'qb-theme';
export const theme = ref(document.documentElement.dataset.theme === 'light' ? 'light' : 'dark');
export function toggleTheme() {
  theme.value = theme.value === 'dark' ? 'light' : 'dark';
  document.documentElement.dataset.theme = theme.value;
  try { localStorage.setItem(THEME_KEY, theme.value); } catch (e) { /* 忽略 */ }
}

export const meta = reactive({ permissions: [], presets: [], gateway: '' });
export const capabilities = reactive({ localAdmin: false, localServerCount: 0 });

export function clearSession() {
  token.value = '';
  session.value = null;
  localStorage.removeItem(TOKEN_KEY);
}

export async function api(path, options = {}) {
  const headers = Object.assign({ 'Content-Type': 'application/json' }, options.headers || {});
  if (token.value) headers['Authorization'] = 'Bearer ' + token.value;

  const res = await fetch('/api' + path, Object.assign({}, options, { headers }));

  if (res.status === 401) {
    clearSession();
    throw new Error('登录状态已失效，请重新登录');
  }

  const text = await res.text();
  let data = null;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch (e) {
      data = { raw: text };
    }
  }

  if (!res.ok) {
    const errMsg = data && (data.error || data.message || data.detail || data.title);
    throw new Error(errMsg || ('请求失败 (' + res.status + ')'));
  }
  return data;
}

export function can(key) {
  if (!key) return true;
  return !!(session.value && session.value.permissionKeys && session.value.permissionKeys.includes(key));
}

export function fmtTime(v) {
  if (!v) return '-';
  const d = new Date(v);
  return isNaN(d.getTime()) ? '-' : d.toLocaleString('zh-CN', { hour12: false });
}

export function permissionName(key) {
  const p = meta.permissions.find(x => x.key === key);
  return p ? p.name : key;
}

export function randomPassword(len) {
  len = len || 12;
  const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789';
  const buf = new Uint32Array(len);
  crypto.getRandomValues(buf);
  let out = '';
  for (let i = 0; i < len; i++) out += chars[buf[i] % chars.length];
  return out;
}

export async function loadMeta() {
  const data = await api('/meta');
  meta.permissions = data.permissions || [];
  meta.presets = data.presets || [];
  meta.gateway = data.gateway || '';
  const caps = data.capabilities || {};
  capabilities.localAdmin = !!caps.localAdminGateway;
  capabilities.localServerCount = caps.localServerCount || 0;
}
