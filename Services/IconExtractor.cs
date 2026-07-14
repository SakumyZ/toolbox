using System;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace ToolBox.Services
{
    /// <summary>
    /// 提供从 Windows 文件（.exe、.dll、.ico）中提取图标字节数据的工具类。
    /// </summary>
    public static class IconExtractor
    {
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, int nIcons);

        [DllImport("user32.dll", EntryPoint = "DestroyIcon", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>
        /// 后台安全提取图标的 PNG 二进制字节数组。此方法可在非 UI 线程中执行。
        /// </summary>
        /// <param name="iconPath">注册表中的图标文件路径或 DLL 资源路径。</param>
        /// <returns>提取出的 PNG 字节数组；若失败则返回 null。</returns>
        public static byte[]? ExtractIconBytes(string iconPath)
        {
            // 分支注释：如果图标路径为空，直接返回 null
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return null;
            }

            try
            {
                string filePath = iconPath.Trim('\"', ' ');
                int iconIndex = 0;

                // 分支注释：如果路径中包含资源索引逗号（如 "C:\path.exe,0"），提取文件路径与索引号
                if (filePath.Contains(","))
                {
                    int commaIndex = filePath.LastIndexOf(',');
                    string indexStr = filePath.Substring(commaIndex + 1);
                    // 分支注释：如果逗号后面的索引解析成功，进行赋值
                    if (int.TryParse(indexStr, out int parsedIndex))
                    {
                        iconIndex = parsedIndex;
                        filePath = filePath.Substring(0, commaIndex).Trim('\"', ' ');
                    }
                }

                // 分支注释：如果路径中包含环境变量（如 %SystemRoot%），进行全局展开
                if (filePath.Contains("%"))
                {
                    filePath = Environment.ExpandEnvironmentVariables(filePath);
                }

                // 分支注释：如果指向的文件不存在，且不是 DLL 动态链接库，则判定为无效路径返回 null
                if (!File.Exists(filePath) && !filePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                IntPtr[] largeIcons = new IntPtr[1];
                int count = ExtractIconEx(filePath, iconIndex, largeIcons, null, 1);

                // 分支注释：如果 ExtractIconEx 返回的数量小于等于 0，或获取的句柄为空，代表未成功提取到图标
                if (count <= 0 || largeIcons[0] == IntPtr.Zero)
                {
                    return null;
                }

                IntPtr hIcon = largeIcons[0];
                try
                {
                    // 使用 System.Drawing.Icon 安全包装 Windows HICON 句柄
                    using (var icon = System.Drawing.Icon.FromHandle(hIcon))
                    {
                        using (var bitmap = icon.ToBitmap())
                        {
                            using (var ms = new MemoryStream())
                            {
                                // 以 PNG 格式编码写入内存流，保证透明度通道正常，并转成 byte 数组返回
                                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                return ms.ToArray();
                            }
                        }
                    }
                }
                finally
                {
                    // 分支注释：必须显式销毁 HICON 句柄，以防 Windows 发生 GDI 对象及内存泄漏
                    DestroyIcon(hIcon);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "后台提取图标字节数据发生异常: {Path}", iconPath);
            }

            return null;
        }
    }
}
