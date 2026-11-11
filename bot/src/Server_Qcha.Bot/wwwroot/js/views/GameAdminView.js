import { ref, reactive, computed, onMounted, ElMessage, ElMessageBox } from '../deps.js';
import { api, can } from '../api.js';

const clone = value => JSON.parse(JSON.stringify(value));
const newGroup = () => ({ key: '', badge: '', color: 'default', kickPower: 0, requiredKickPower: 0, hiddenByDefault: false, cover: true, permissions: [], pluginsEnabled: false, pluginPermissions: [], inheritance: [], shared: false, pluginOnly: false });
const newMember = () => ({ userId: '', group: '', pluginsEnabled: null, allow: [], deny: [], pluginAllow: [], pluginDeny: [], shared: false });
const match = (pattern, node) => pattern === '.*' || pattern === '*' || pattern.toLowerCase() === node.toLowerCase() || (pattern.endsWith('.*') && node.toLowerCase().startsWith(pattern.slice(0, -1).toLowerCase()));

export const GameAdminView = {
  name: 'GameAdminView',
  setup() {
    const servers = ref([]), selected = ref(null), snapshot = ref(null), busy = ref(false), error = ref(''), tab = ref('members'), search = ref('');
    const dialog = reactive({ open: false, kind: 'group', creating: true, original: '', target: null, snapshot: null, form: newGroup(), filter: '', node: '', saving: false, loading: false });
    const server = computed(() => servers.value.find(s => s.index === selected.value));
    const groups = computed(() => (snapshot.value?.groups || []).filter(g => `${g.key} ${g.badge}`.toLowerCase().includes(search.value.toLowerCase())));
    const members = computed(() => (snapshot.value?.members || []).filter(m => `${m.userId} ${m.group}`.toLowerCase().includes(search.value.toLowerCase())));
    const catalog = computed(() => (dialog.snapshot?.catalog || []).filter(p => `${p.key} ${p.name}`.toLowerCase().includes(dialog.filter.toLowerCase())));
    const selectedGroup = computed(() => dialog.snapshot?.groups.find(g => g.key === dialog.form.group));
    const nativeUnsupported = computed(() => dialog.kind === 'group' ? dialog.form.permissions.filter(k => !dialog.snapshot?.catalog.some(p => p.key === k)) : []);
    const inheritanceUnsupported = computed(() => dialog.kind === 'group' ? dialog.form.inheritance.filter(k => !dialog.snapshot?.groups.some(g => g.key === k)) : []);
    const pluginNodes = computed(() => [...new Set([...(dialog.snapshot?.groups || []).flatMap(g => g.pluginPermissions), ...(dialog.form.pluginAllow || []), ...(dialog.form.pluginDeny || [])])].sort());
    function headers(index) { return { 'X-Qcha-Server': servers.value.find(s => s.index === index)?.identity || '' }; }
    async function read(index) { return (await api(`/servers/${index}/game-admin`, { headers: headers(index) })).snapshot; }
    async function loadServers() {
      try { servers.value = await api('/game-admin/servers'); if (selected.value === null) selected.value = servers.value[0]?.index ?? null; await load(); }
      catch (e) { error.value = e.message; }
    }
    let generation = 0;
    async function load() {
      const own = ++generation, index = selected.value;
      snapshot.value = null; error.value = ''; busy.value = true;
      try { if (index != null) { const value = await read(index); if (own === generation) snapshot.value = value; } }
      catch (e) { if (own === generation) error.value = e.message; }
      finally { if (own === generation) busy.value = false; }
    }
    function open(kind, item = null, copy = false) {
      Object.assign(dialog, { open: true, kind, creating: !item || copy, original: item?.key || item?.userId || '', target: selected.value, snapshot: clone(snapshot.value), filter: '', node: '', saving: false, loading: false });
      dialog.form = clone(item || (kind === 'group' ? newGroup() : newMember()));
      if (copy) { dialog.form.key += '_copy'; dialog.form.shared = false; dialog.form.pluginOnly = false; }
    }
    let targetGeneration = 0;
    async function changeTarget() {
      const own = ++targetGeneration;
      dialog.loading = true; dialog.snapshot = null;
      try { const value = await read(dialog.target); if (own === targetGeneration) dialog.snapshot = value; }
      catch (e) { ElMessage.error(e.message); }
      finally { if (own === targetGeneration) dialog.loading = false; }
    }
    function mode(key, plugin = false) {
      const allow = plugin ? dialog.form.pluginAllow : dialog.form.allow, deny = plugin ? dialog.form.pluginDeny : dialog.form.deny;
      return deny.includes(key) ? 'deny' : allow.includes(key) ? 'allow' : 'inherit';
    }
    function setMode(key, value, plugin = false) {
      const a = plugin ? 'pluginAllow' : 'allow', d = plugin ? 'pluginDeny' : 'deny';
      dialog.form[a] = dialog.form[a].filter(k => k !== key); dialog.form[d] = dialog.form[d].filter(k => k !== key);
      if (value === 'allow') dialog.form[a].push(key); if (value === 'deny') dialog.form[d].push(key);
    }
    function inheritedNodes(group, visited = new Set()) {
      if (!group || visited.has(group.key)) return [];
      visited.add(group.key);
      return [...group.pluginPermissions, ...group.inheritance.flatMap(k => inheritedNodes(dialog.snapshot?.groups.find(g => g.key === k), visited))];
    }
    function effective(key, plugin = false) {
      if (!plugin) {
        const m = mode(key); return m === 'deny' ? '拒绝 · 个人' : m === 'allow' ? '允许 · 个人' : selectedGroup.value?.permissions.includes(key) ? '允许 · 权限组' : '未授权 · 权限组';
      }
      if (!(dialog.form.pluginsEnabled ?? selectedGroup.value?.pluginsEnabled)) return '拒绝 · 总开关';
      if (dialog.form.pluginDeny.some(p => match(p, key))) return '拒绝 · 个人';
      if (dialog.form.pluginAllow.some(p => match(p, key))) return '允许 · 个人';
      return inheritedNodes(selectedGroup.value).some(p => match(p, key)) ? '允许 · 权限组/继承' : '未授权 · 权限组';
    }
    function addNode() {
      const node = dialog.node.trim(); if (!node) return;
      if (!/^[A-Za-z0-9_.:*\-]+$/.test(node) || node.length > 160) { ElMessage.error('权限节点格式无效'); return; }
      if (!dialog.form.pluginAllow.includes(node)) dialog.form.pluginAllow.push(node);
      dialog.form.pluginDeny = dialog.form.pluginDeny.filter(n => n !== node); dialog.node = '';
    }
    function resetOverrides() { Object.assign(dialog.form, { allow: [], deny: [], pluginAllow: [], pluginDeny: [], pluginsEnabled: null }); }
    async function mutate(index, source, operation, data) {
      const request = { operation, revision: source.revision, requestId: crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`, ...data };
      return api(`/servers/${index}/game-admin/changes`, { method: 'POST', headers: headers(index), body: JSON.stringify(request) });
    }
    async function save() {
      if (dialog.saving || !dialog.snapshot) return;
      if (nativeUnsupported.value.length || inheritanceUnsupported.value.length) { ElMessage.error('请先移除目标服不支持的权限或继承'); return; }
      dialog.saving = true;
      try {
        const op = dialog.kind === 'group' ? dialog.creating ? 'create-group' : 'update-group' : 'set-member';
        const payload = dialog.kind === 'group' ? { key: dialog.original, group: dialog.form } : { member: dialog.form };
        const result = await mutate(dialog.target, dialog.snapshot, op, payload);
        if (!result.persisted || !result.applied) throw new Error('保存结果不完整，请重新读取核实');
        dialog.open = false; ElMessage.success('已保存并生效'); await load();
      } catch (e) {
        ElMessage.error(e.message);
        dialog.snapshot = null; await load();
      } finally { dialog.saving = false; }
    }
    async function remove(kind, item) {
      try { await ElMessageBox.confirm(`删除${kind === 'group' ? '权限组 ' + item.key : '管理员 ' + item.userId}？`, '确认删除', { type: 'warning' }); }
      catch { return; }
      busy.value = true;
      try { await mutate(selected.value, snapshot.value, kind === 'group' ? 'delete-group' : 'delete-member', { key: kind === 'group' ? item.key : item.userId }); ElMessage.success('已删除并生效'); }
      catch (e) { ElMessage.error(e.message); }
      finally { await load(); }
    }
    onMounted(() => { if (can('game-admin.manage')) loadServers(); });
    return { can, servers, selected, server, snapshot, busy, error, tab, search, groups, members, dialog, catalog, selectedGroup, nativeUnsupported, inheritanceUnsupported, pluginNodes,
      load, loadServers, open, changeTarget, mode, setMode, effective, addNode, resetOverrides, save, remove };
  },
  template: `
  <section v-if="can('game-admin.manage')" class="game-admin">
    <div class="page-header">
      <div class="page-header-meta">
        <h3 class="page-title">游戏内管理员与权限组</h3>
      </div>
      <div class="page-actions">
        <el-select v-model="selected" placeholder="选择目标服务器" @change="load" :disabled="busy" style="width:240px">
          <el-option v-for="s in servers" :key="s.identity" :value="s.index" :label="s.name + (s.isOnline ? '' : ' · 离线')" />
        </el-select>
        <el-button @click="loadServers" :loading="busy">刷新配置</el-button>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon style="margin-bottom:14px" />
    <el-card v-if="!busy && !snapshot && !error" shadow="never">
      <el-empty description="尚无已连接的在线服务器可读取权限配置" :image-size="72" />
    </el-card>

    <div v-if="snapshot" v-loading="busy">
      <div class="metric-tiles" style="margin-bottom:14px">
        <div class="metric-tile">
          <span class="metric-tile-label">当前目标服务器</span>
          <span class="metric-tile-value" style="display:flex;align-items:center;gap:6px">
            <span class="dot" :class="server?.isOnline ? 'dot-on' : 'dot-off'"></span>
            {{ server?.name || ('#' + selected) }}
          </span>
        </div>
        <div class="metric-tile">
          <span class="metric-tile-label">游戏权限框架</span>
          <span class="metric-tile-value"><el-tag size="small" type="primary">{{ snapshot.framework || 'LabAPI' }}</el-tag></span>
        </div>
        <div class="metric-tile">
          <span class="metric-tile-label">管理员成员数</span>
          <span class="metric-tile-value num">{{ (snapshot.members || []).length }} 人</span>
        </div>
        <div class="metric-tile">
          <span class="metric-tile-label">权限组 / 预设数</span>
          <span class="metric-tile-value num">{{ (snapshot.groups || []).length }} 组</span>
        </div>
      </div>

      <el-alert :title="snapshot.framework + ' · 插件总开关和节点设置仅控制框架标准权限（LabAPI 默认提供器），不控制插件自建权限系统。'" type="info" :closable="false" show-icon style="margin-bottom:14px" />

      <el-card shadow="never">
        <div class="ga-toolbar" style="justify-content:space-between;margin-top:0">
          <el-input v-model="search" placeholder="搜索 UserID、组标识或游戏内称号..." clearable style="max-width:360px" />
          <el-button type="primary" :disabled="busy || !server?.isOnline" @click="open(tab === 'groups' ? 'group' : 'member')">
            {{ tab === 'groups' ? '+ 新建权限组 / 预设' : '+ 添加管理员' }}
          </el-button>
        </div>

        <el-tabs v-model="tab">
          <el-tab-pane name="members">
            <template #label>
              <span>管理员成员 ({{ members.length }})</span>
            </template>
            <el-table :data="members" border stripe empty-text="暂无管理员，可通过持久 UserID 添加离线玩家">
              <el-table-column prop="userId" label="持久 UserID" min-width="220">
                <template #default="{row}">
                  <code style="font-size:12px">{{ row.userId }}</code>
                </template>
              </el-table-column>
              <el-table-column prop="group" label="所属权限组" min-width="150">
                <template #default="{row}">
                  <span class="ga-badge-pill">{{ row.group || '未分配' }}</span>
                </template>
              </el-table-column>
              <el-table-column label="个人权限覆盖" width="150" align="center">
                <template #default="{row}">
                  <el-tag size="small" :type="(row.allow.length + row.deny.length + row.pluginAllow.length + row.pluginDeny.length + (row.pluginsEnabled === null ? 0 : 1)) > 0 ? 'warning' : 'info'">
                    {{ row.allow.length + row.deny.length + row.pluginAllow.length + row.pluginDeny.length + (row.pluginsEnabled === null ? 0 : 1) }} 项覆盖
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="180">
                <template #default="{row}">
                  <el-tag v-if="row.shared" size="small" type="info">共享配置</el-tag>
                  <template v-else>
                    <el-button link type="primary" :disabled="busy" @click="open('member',row)">编辑权限</el-button>
                    <el-button link type="danger" :disabled="busy" @click="remove('member',row)">移除</el-button>
                  </template>
                </template>
              </el-table-column>
            </el-table>
          </el-tab-pane>

          <el-tab-pane name="groups">
            <template #label>
              <span>权限组 / 预设 ({{ groups.length }})</span>
            </template>
            <el-table :data="groups" border stripe empty-text="暂无权限组">
              <el-table-column prop="key" label="组标识 (Key)" min-width="150">
                <template #default="{row}">
                  <b class="mono">{{ row.key }}</b>
                </template>
              </el-table-column>
              <el-table-column prop="badge" label="游戏内称号" min-width="160">
                <template #default="{row}">
                  <span class="ga-badge-pill">{{ row.badge || '-' }}</span>
                  <span v-if="row.color && row.color !== 'default'" class="muted" style="margin-left:6px;font-size:11px">({{ row.color }})</span>
                </template>
              </el-table-column>
              <el-table-column label="踢人权重" width="130" align="center">
                <template #default="{row}">
                  <span class="mono" style="font-size:12px">{{ row.kickPower }} / {{ row.requiredKickPower }}</span>
                </template>
              </el-table-column>
              <el-table-column label="权限概况" min-width="200">
                <template #default="{row}">
                  <div>
                    <el-tag size="small" type="info" style="margin-right:6px">{{ row.permissions.length }} 项原生</el-tag>
                    <el-tag size="small" :type="row.pluginsEnabled ? 'success' : 'info'">插件{{ row.pluginsEnabled ? '已开启' : '关闭' }}</el-tag>
                  </div>
                  <div v-if="row.shared" class="muted" style="font-size:11px;margin-top:3px">共享配置（复制后可编辑）</div>
                  <div v-if="row.pluginOnly" class="muted" style="font-size:11px;margin-top:3px">仅插件权限组</div>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="210">
                <template #default="{row}">
                  <el-button link type="primary" :disabled="busy || row.shared" @click="open('group',row)">编辑</el-button>
                  <el-button link type="primary" :disabled="busy" @click="open('group',row,true)">复制/跨服</el-button>
                  <el-button link type="danger" :disabled="busy || row.shared || row.pluginOnly" @click="remove('group',row)">删除</el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-tab-pane>
        </el-tabs>
      </el-card>
    </div>

    <el-dialog v-model="dialog.open" :title="dialog.kind === 'group' ? '权限组／预设设置' : '管理员权限设置'" width="min(920px, 95vw)" class="ga-dialog" :close-on-click-modal="false" :close-on-press-escape="!dialog.saving" :show-close="!dialog.saving">
      <el-alert v-if="!dialog.snapshot && !dialog.loading" title="配置版本已失效或读取失败。请关闭窗口并重新打开；当前输入保留供核对。" type="warning" :closable="false" style="margin-bottom:12px" />
      <el-form label-position="top" :disabled="dialog.saving || dialog.loading">
        <template v-if="dialog.kind === 'group'">
          <el-form-item v-if="dialog.creating" label="目标服务器（只复制组配置，不复制成员）">
            <el-select v-model="dialog.target" @change="changeTarget" style="width:100%">
              <el-option v-for="s in servers" :key="s.identity" :label="s.name" :value="s.index" :disabled="!s.isOnline" />
            </el-select>
          </el-form-item>
          <div class="ga-grid">
            <el-form-item label="组标识 (Key)"><el-input v-model="dialog.form.key" :disabled="!dialog.creating" maxlength="64" placeholder="例如 trainee / moderator" /></el-form-item>
            <el-form-item label="游戏内称号 (Badge)"><el-input v-model="dialog.form.badge" maxlength="128" placeholder="游戏内显示的管理员头衔" /></el-form-item>
            <el-form-item label="颜色名称 (Color)"><el-input v-model="dialog.form.color" placeholder="例如 red / silver / cyan / default" /></el-form-item>
          </div>
          <div class="ga-grid">
            <el-form-item label="踢人等级 (KickPower)"><el-input-number v-model="dialog.form.kickPower" :min="0" :max="255" style="width:100%" /></el-form-item>
            <el-form-item label="防踢等级 (RequiredKickPower)"><el-input-number v-model="dialog.form.requiredKickPower" :min="0" :max="255" style="width:100%" /></el-form-item>
            <el-form-item label="称号展示选项">
              <div style="display:flex;gap:14px;padding-top:4px">
                <el-checkbox v-model="dialog.form.hiddenByDefault">默认隐藏称号</el-checkbox>
                <el-checkbox v-model="dialog.form.cover">覆盖全局称号</el-checkbox>
              </div>
            </el-form-item>
          </div>
        </template>
        <template v-else>
          <div class="ga-grid">
            <el-form-item label="持久 UserID（支持离线玩家）">
              <el-input v-model="dialog.form.userId" :disabled="!dialog.creating" placeholder="7656119…@steam" />
            </el-form-item>
            <el-form-item label="所属权限组">
              <el-select v-model="dialog.form.group" filterable style="width:100%">
                <el-option v-for="g in (dialog.snapshot?.groups || []).filter(g => !g.pluginOnly && !g.shared)" :key="g.key" :label="g.badge + ' · ' + g.key" :value="g.key" />
              </el-select>
            </el-form-item>
            <el-form-item label="覆盖重置">
              <el-button @click="resetOverrides" style="width:100%">全部恢复为跟随权限组</el-button>
            </el-form-item>
          </div>
        </template>

        <h3>游戏原生权限</h3>
        <div class="ga-toolbar">
          <el-input v-model="dialog.filter" placeholder="搜索权限名称或键名..." clearable style="max-width:340px" />
          <template v-if="dialog.kind === 'group'">
            <el-button size="small" @click="dialog.form.permissions = [...new Set([...dialog.form.permissions,...catalog.map(p => p.key)])]">全选当前筛选</el-button>
            <el-button size="small" @click="dialog.form.permissions = []">清空全部</el-button>
          </template>
        </div>

        <template v-if="dialog.kind === 'group'">
          <el-alert v-if="nativeUnsupported.length" :title="'目标服不支持：' + nativeUnsupported.join('、')" type="error" :closable="false" style="margin-bottom:8px" />
          <div v-if="nativeUnsupported.length" style="margin-bottom:8px;display:flex;gap:6px;flex-wrap:wrap">
            <el-tag v-for="p in nativeUnsupported" :key="p" type="danger" closable @close="dialog.form.permissions = dialog.form.permissions.filter(x => x !== p)">{{p}}</el-tag>
          </div>
          <el-checkbox-group v-model="dialog.form.permissions" class="ga-permissions">
            <el-checkbox v-for="p in catalog" :key="p.key" :label="p.key">
              <span>{{p.name}}<small>{{p.key}}</small></span>
            </el-checkbox>
          </el-checkbox-group>
        </template>
        <el-table v-else :data="catalog" border size="small" max-height="320">
          <el-table-column label="权限项" min-width="200">
            <template #default="{row}">
              <div style="font-weight:600">{{ row.name }}</div>
              <small class="mono muted">{{ row.key }}</small>
            </template>
          </el-table-column>
          <el-table-column label="个人设置" width="150">
            <template #default="{row}">
              <el-select :model-value="mode(row.key)" @update:model-value="v => setMode(row.key,v)" size="small">
                <el-option label="跟随组" value="inherit" />
                <el-option label="允许" value="allow" />
                <el-option label="拒绝" value="deny" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="最终权限／来源" min-width="170">
            <template #default="{row}">
              <el-tag size="small" :type="effective(row.key).startsWith('允许') ? 'success' : effective(row.key).startsWith('拒绝') ? 'danger' : 'info'">
                {{effective(row.key)}}
              </el-tag>
            </template>
          </el-table-column>
        </el-table>

        <h3>插件权限节点</h3>
        <el-form-item label="插件权限总开关">
          <el-switch v-if="dialog.kind === 'group'" v-model="dialog.form.pluginsEnabled" active-text="允许插件权限" inactive-text="关闭" />
          <el-select v-else :model-value="dialog.form.pluginsEnabled === null ? 'inherit' : dialog.form.pluginsEnabled ? 'allow' : 'deny'" @update:model-value="v => dialog.form.pluginsEnabled = v === 'inherit' ? null : v === 'allow'" style="max-width:240px">
            <el-option label="跟随权限组" value="inherit" />
            <el-option label="允许" value="allow" />
            <el-option label="关闭" value="deny" />
          </el-select>
          <div class="muted" style="margin-left:12px">关闭后保留节点配置，仅管理框架标准权限。</div>
        </el-form-item>

        <template v-if="dialog.kind === 'group'">
          <el-form-item label="插件权限节点（输入后回车添加，支持通配符 .*）">
            <el-select v-model="dialog.form.pluginPermissions" multiple filterable allow-create default-first-option style="width:100%" placeholder="输入例如 exiled.* 或回车创建新节点">
              <el-option v-for="p in pluginNodes" :key="p" :label="p" :value="p" />
            </el-select>
          </el-form-item>
          <el-form-item label="继承其他组的插件节点">
            <el-select v-model="dialog.form.inheritance" multiple filterable style="width:100%" placeholder="选择要继承的权限组">
              <el-option v-for="g in (dialog.snapshot?.groups || []).filter(g => g.key !== dialog.form.key)" :key="g.key" :label="g.key" :value="g.key" />
            </el-select>
          </el-form-item>
          <el-alert v-if="inheritanceUnsupported.length" :title="'目标服缺少继承组：' + inheritanceUnsupported.join('、') + '，请从继承列表中移除或先复制对应组。'" type="error" :closable="false" />
        </template>
        <template v-else>
          <div class="ga-toolbar">
            <el-input v-model="dialog.node" placeholder="输入插件权限节点，回车添加允许项" style="max-width:350px" @keyup.enter.prevent="addNode" />
            <el-button @click="addNode">添加允许节点</el-button>
          </div>
          <el-table :data="pluginNodes.map(key => ({key}))" border size="small" max-height="280">
            <el-table-column prop="key" label="权限节点" min-width="200">
              <template #default="{row}"><code style="font-size:12px">{{ row.key }}</code></template>
            </el-table-column>
            <el-table-column label="个人设置" width="150">
              <template #default="{row}">
                <el-select :model-value="mode(row.key,true)" @update:model-value="v => setMode(row.key,v,true)" size="small">
                  <el-option label="跟随组" value="inherit" />
                  <el-option label="允许" value="allow" />
                  <el-option label="拒绝" value="deny" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="最终权限／来源" min-width="170">
              <template #default="{row}">
                <el-tag size="small" :type="effective(row.key,true).startsWith('允许') ? 'success' : effective(row.key,true).startsWith('拒绝') ? 'danger' : 'info'">
                  {{effective(row.key,true)}}
                </el-tag>
              </template>
            </el-table-column>
          </el-table>
        </template>
      </el-form>
      <template #footer>
        <el-button :disabled="dialog.saving" @click="dialog.open=false">关闭</el-button>
        <el-button type="primary" :loading="dialog.saving" :disabled="!dialog.snapshot || dialog.loading || nativeUnsupported.length > 0 || inheritanceUnsupported.length > 0" @click="save">保存并同步生效</el-button>
      </template>
    </el-dialog>
  </section>`
};
