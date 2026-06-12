namespace ToolBox.Models
{
    /// <summary>
    /// WebDAV 同步配置实体
    /// </summary>
    public class WebDavSettings
    {
        /// <summary>
        /// WebDAV 服务器 URL
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用启动和退出时的自动同步
        /// </summary>
        public bool IsAutoSyncEnabled { get; set; } = false;

        /// <summary>
        /// 备份目录 (可选)
        /// </summary>
        public string Directory { get; set; } = string.Empty;

        /// <summary>
        /// 最多保留备份数
        /// </summary>
        public int MaxBackupCount { get; set; } = 10;
    }
}
