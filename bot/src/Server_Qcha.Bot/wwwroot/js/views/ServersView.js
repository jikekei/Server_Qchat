import { useServers } from '../modules/useServers.js';
import { meta, can, fmtTime } from '../api.js';

export const ServersView = {
  name: 'ServersView',
  template: `
        <div>
          <div class="page-header">
            <div class="page-header-info">
              <h2>在线服务器</h2>
            </div>
            <div class="page-header-actions">
              <el-tag type="info" effect="plain">在线实例：{{ servers.length }} 台</el-tag>
              <el-tag v-if="meta.gateway" type="info" effect="plain">命令网关：{{ meta.gateway }}</el-tag>
              <el-button type="primary" plain :loading="loadingServers" @click="loadServers">刷新列表</el-button>
            </div>
          </div>

          <el-empty v-if="servers.length === 0" description="当前没有服务器在线（等待游戏服 EXILED / LabAPI 插件注册连接）"></el-empty>

          <div class="server-grid">
            <el-card v-for="s in servers" :key="s.index" class="server-card interactive" shadow="hover">
              <div class="server-head">
                <div class="server-name-wrap">
                  <span class="server-index-badge">#{{ s.index }}</span>
                  <span class="server-name">{{ s.name }}</span>
                  <el-tag v-if="s.isStatic" size="small" type="info">静态配置</el-tag>
                </div>
                <div>
                  <span class="pulse-dot"></span>
                  <el-tag size="small" type="success" effect="dark">在线</el-tag>
                </div>
              </div>

              <div class="server-meta">
                <span>🔌 {{ s.connectHost }}:{{ s.port }}</span>
                <span v-if="s.gamePort">· 🎮 游戏端口 {{ s.gamePort }}</span>
                <span v-if="s.lastHeartbeat">· 💓 {{ fmtTime(s.lastHeartbeat) }}</span>
              </div>

              <div v-if="s.maxPlayers !== undefined || s.onlinePlayers !== undefined" class="server-capacity-box">
                <div class="server-capacity-top">
                  <span class="muted">当前在线玩家</span>
                  <b class="mono">{{ s.onlinePlayers ?? 0 }} / {{ s.maxPlayers || '-' }} 人</b>
                </div>
                <el-progress
                  :percentage="s.maxPlayers > 0 ? Math.min(100, Math.round(((s.onlinePlayers || 0) / s.maxPlayers) * 100)) : 0"
                  :stroke-width="8"
                  :show-text="false">
                </el-progress>
              </div>

              <div class="server-actions">
                <el-button size="small" type="primary" plain @click="openPlayers(s)">👥 玩家列表</el-button>
                <el-button size="small" @click="openInfo(s)">📋 服务器详情</el-button>
                <el-button size="small" type="primary" v-if="can('broadcast.send')" @click="openBroadcast(s)">📢 全服广播</el-button>
                <el-button size="small" type="success" plain v-if="can('round.control')" @click="doRound(s, 'start')">▶ 开始回合</el-button>
                <el-button size="small" type="warning" plain v-if="can('round.control')" @click="doRound(s, 'restart')">↻ 重启回合</el-button>
              </div>
            </el-card>
          </div>
        </div>
`,
  setup() {
    const serversModule = useServers();
    return {
      ...serversModule,
      meta,
      can,
      fmtTime,
    };
  }
};
