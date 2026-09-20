import { useServers } from '../modules/useServers.js';
import { meta, can } from '../api.js';

export const ServersView = {
  name: 'ServersView',
  template: `
        <div>
          <div class="toolbar">
            <el-button :loading="loadingServers" @click="loadServers">刷新</el-button>
            <span class="muted">在线服务器 {{ servers.length }} 台</span>
            <span class="muted">命令网关：{{ meta.gateway || '-' }}</span>
          </div>

          <el-empty v-if="servers.length === 0" description="当前没有服务器在线（等待游戏服插件注册）"></el-empty>

          <div class="server-grid">
            <el-card v-for="s in servers" :key="s.index" class="server-card" shadow="hover">
              <div class="server-head">
                <span class="server-name">#{{ s.index }} {{ s.name }}</span>
                <el-tag size="small" type="success" effect="dark">在线</el-tag>
              </div>
              <div class="server-meta">
                {{ s.connectHost }}:{{ s.port }}
                <span v-if="s.gamePort"> · 游戏端口 {{ s.gamePort }}</span>
                <span v-if="s.isStatic"> · 静态配置</span>
              </div>
              <div class="server-actions">
                <el-button size="small" @click="openPlayers(s)">玩家列表</el-button>
                <el-button size="small" @click="openInfo(s)">服务器详情</el-button>
                <el-button size="small" type="primary" v-if="can('broadcast.send')" @click="openBroadcast(s)">广播</el-button>
                <el-button size="small" v-if="can('round.control')" @click="doRound(s, 'start')">开始回合</el-button>
                <el-button size="small" v-if="can('round.control')" @click="doRound(s, 'restart')">重启回合</el-button>
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
    };
  }
};
