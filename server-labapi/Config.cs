using System.ComponentModel;

namespace SocketServer
{
    /// <summary>
    /// 插件配置。
    /// <para>
    /// LabAPI 相比 EXILED 的一大变化：<b>不再有 IConfig 接口</b>。
    /// 配置就是一个普通的 POCO，LabAPI 用 YamlDotNet（下划线命名规则）自动读写：
    ///   <c>configs/&lt;服务器端口&gt;/Server_Qcha/config.yml</c>
    /// 首次启动若文件不存在会自动生成默认值；每次启动都会回写以补齐新增字段。
    /// </para>
    /// <para>
    /// 因此原来配置里的 <c>is_enabled</c> 已移除——插件是否启用改由同目录下的
    /// <c>properties.yml</c>（字段 <c>is_enabled</c>）控制，那是 LabAPI 的保留文件，
    /// 不要写进本类。
    /// </para>
    /// <para>
    /// [Description] 会被 LabAPI 的 YAML 序列化器输出成字段上方的注释，
    /// 与 EXILED 时代行为一致；属性名转下划线后与旧配置完全对应，
    /// 所以旧的 <c>config.yml</c> 可以直接复用。
    /// </para>
    /// </summary>
    public sealed class Config
    {
        [Description("设置为起始TCP监听端口，若被占用将自动递增探测")]
        public int TcpPort { get; set; } = 10087;

        [Description("设置为服务器IP 一般不用改")]
        public string IP { get; set; } = "127.0.0.1";

        [Description("设置为服务器名称（如：1服、测试服等）")]
        public string ServerName { get; set; } = "1服";

        [Description("显示DisplayMode为1时显的东西")]
        public string ContentText { get; set; } = "";

        [Description("0显示时间 1显示ContentText里面的东西 2空白")]
        public int DisplayMode { get; set; } = 2;

        [Description("QQ机器人后台监听服务的IP")]
        public string BotIP { get; set; } = "127.0.0.1";

        [Description("QQ机器人后台监听服务的端口号")]
        public int BotPort { get; set; } = 10088;

        [Description("安全验证Token，须与机器人端的Token一致")]
        public string AuthToken { get; set; } = "QchaSecret_123";

        [Description("排序权重，>0时按此排序，=0时自动排序")]
        public int SortOrder { get; set; } = 0;

        [Description("机器人回连本服务器使用的IP地址（跨机部署须手动配置，同机留空即可）")]
        public string ConnectHost { get; set; } = "";

        [Description("输出调试日志（命令收发、心跳明细等）")]
        public bool Debug { get; set; } = false;
    }
}
