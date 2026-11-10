import { ref, onMounted } from '../deps.js';
import { api, can } from '../api.js';
export const OperationsView = {
  template: `<el-card v-if="can('accounts.manage')" style="margin-top:20px">
    <h3>API 接口配置</h3><p>通过独立 Bearer 令牌访问运维接口。令牌仅在创建或重置后显示一次，请妥善保存。</p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-switch v-model="enabled" active-text="启用 API" />
    <el-switch v-model="control" active-text="允许启动、停止和重启服务器" style="margin-left:20px" />
    <p><el-button @click="save(false)">保存</el-button><el-button @click="save(true)">生成 / 重置令牌</el-button></p>
    <el-alert v-if="key" type="success" :closable="false" title="新令牌（重置后旧令牌立即失效）" /><pre v-if="key" style="white-space:pre-wrap;overflow-wrap:anywhere">{{key}}</pre>
    <p>{{message}}</p><p>请求头：Authorization: Bearer &lt;令牌&gt;</p>
    <pre style="white-space:pre-wrap">GET /api/ops/v1/panel/logs — 最近 2000 条面板运行日志
GET /api/ops/v1/servers — 服务器状态列表
GET /api/ops/v1/servers/{id} — 单服状态
GET /api/ops/v1/servers/{id}/logs?after=0&amp;limit=500 — 增量服务器日志
GET /api/ops/v1/log-servers — 服务器数量和列表
GET /api/ops/v1/servers/{id}/log-dates — 有日志的所有日期
GET /api/ops/v1/servers/{id}/log-files?date=today — 今天的日志文件
GET /api/ops/v1/servers/{id}/archive?file=文件名&amp;offset=0 — 读取所选文件
POST /api/ops/v1/servers/{id}/start — 启动
POST /api/ops/v1/servers/{id}/stop — 停止
POST /api/ops/v1/servers/{id}/restart — 重启</pre>
    <h3>服务器日志归档（全部，包含 stdout）</h3>
    <p>共 {{servers.length}} 个托管服务器。选择服务器、日期和文件查看日志；日期按服务端时间计算。</p>
    <el-select v-model="serverId" placeholder="选择服务器" style="max-width:300px" @change="loadDates">
      <el-option v-for="s in servers" :key="s.id" :value="s.id" :label="(s.name || s.id) + ' (' + s.id + ')'" />
    </el-select>
    <el-select v-model="selectedDate" placeholder="日期" style="max-width:260px" @change="listFiles">
      <el-option value="today" :label="'今天 ' + today" />
      <el-option value="" label="全部时间" />
      <el-option v-for="d in dates" :key="d.date" :value="d.date" :label="d.date + '（' + d.count + ' 个文件）'" />
    </el-select>
    <el-button @click="listFiles">读取文件列表</el-button>
    <el-select v-model="file" placeholder="选择日志文件" style="width:100%;margin-top:12px" @change="readFile(true)">
      <el-option v-for="f in files" :key="f.name" :value="f.name" :label="f.name" />
    </el-select>
    <p v-if="serverId && !files.length">所选日期暂无日志文件。</p>
    <pre style="max-height:500px;overflow:auto;white-space:pre-wrap">{{content}}</pre>
    <el-button v-if="more" @click="readFile(false)">读取下一页</el-button>
  </el-card>`,
  setup() {
    const enabled=ref(false), control=ref(false), key=ref(''), error=ref(''), message=ref('');
    onMounted(async()=>{ if(!can('accounts.manage'))return; try {const s=await api('/operations/settings');enabled.value=s.enabled;control.value=s.allowControl;} catch(e){error.value=e.message;} });
    async function save(rotateKey){error.value='';try{const s=await api('/operations/settings',{method:'PUT',body:JSON.stringify({enabled:enabled.value,allowControl:control.value,rotateKey})});key.value=s.key||'';message.value='配置已保存，立即生效';}catch(e){error.value=e.message;}}
    const serverId=ref(''),servers=ref([]),dates=ref([]),today=ref(''),selectedDate=ref('today'),files=ref([]),file=ref(''),content=ref(''),more=ref(false);let offset=0,revision=0;
    onMounted(async()=>{if(!can('server.control'))return;try{const r=await api('/local/servers');servers.value=r.servers||[];if(servers.value.length){serverId.value=servers.value[0].id;await loadDates();}}catch(e){error.value=e.message;}});
    async function loadDates(){const id=serverId.value;revision++;dates.value=[];files.value=[];file.value='';content.value='';more.value=false;try{const r=await api('/operations/log-dates/'+encodeURIComponent(id));if(id!==serverId.value)return;dates.value=r.dates;today.value=r.today;await listFiles();}catch(e){error.value=e.message;}}
    async function listFiles(){const current=++revision;file.value='';content.value='';more.value=false;files.value=[];if(!serverId.value)return;try{error.value='';const r=await api('/operations/archive/'+encodeURIComponent(serverId.value)+'?date='+encodeURIComponent(selectedDate.value));if(current!==revision)return;files.value=r.files;}catch(e){if(current===revision)error.value=e.message;}}
    async function readFile(reset){const current=++revision;if(!file.value)return;try{error.value='';if(reset){offset=0;content.value='';}const r=await api('/operations/archive/'+encodeURIComponent(serverId.value)+'?file='+encodeURIComponent(file.value)+'&date='+encodeURIComponent(selectedDate.value)+'&offset='+offset);if(current!==revision)return;content.value=r.lines.join('');offset=r.next;more.value=r.more;}catch(e){if(current===revision)error.value=e.message;}}
    return {can,enabled,control,key,error,message,save,serverId,servers,dates,today,selectedDate,loadDates,files,file,content,more,listFiles,readFile};
  }
};
