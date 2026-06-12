using System;

namespace ToolBox.Models
{
    /// <summary>
    /// WebDAV 上的历史备份节点项
    /// </summary>
    public class WebDavBackupItem
    {
        /// <summary>
        /// 备份文件名 (例如 toolbox_backup_20260611085000.zip)
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// 完整 WebDAV URL
        /// </summary>
        public string FileUrl { get; set; } = string.Empty;

        /// <summary>
        /// 备份最后修改时间（转换为本地时间）
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// 备份文件大小（单位：字节）
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 格式化后的文件大小显示文本
        /// </summary>
        public string DisplaySize => FormatSize(Size);

        /// <summary>
        /// 格式化后的修改时间显示文本
        /// </summary>
        public string DisplayTime => LastModified.ToString("yyyy-MM-dd HH:mm:ss");

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024)
                return $"{bytes / 1024.0 / 1024.0:F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }
    }
}
