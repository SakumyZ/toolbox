using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ToolBox.Models;
using Serilog;

namespace ToolBox.Services
{
    /// <summary>
    /// 同步结果枚举
    /// </summary>
    public enum SyncResult
    {
        /// <summary>
        /// 已上传本地最新配置到云端
        /// </summary>
        Uploaded,
        /// <summary>
        /// 已从云端下载并恢复最新配置
        /// </summary>
        Downloaded,
        /// <summary>
        /// 本地与云端已同步，无须操作
        /// </summary>
        AlreadySynced,
        /// <summary>
        /// 同步失败
        /// </summary>
        Failed
    }

    /// <summary>
    /// 配置备份与 WebDAV 同步服务
    /// </summary>
    public class BackupService
    {
        private const string WebDavUrlKey = "webdav_url";
        private const string WebDavUsernameKey = "webdav_username";
        private const string WebDavPasswordKey = "webdav_password";
        private const string WebDavAutoSyncKey = "webdav_auto_sync";
        private const string WebDavDirectoryKey = "webdav_directory";

        private readonly string _appDataPath;
        private readonly string _connectionString;

        public BackupService()
        {
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolBox");
            Directory.CreateDirectory(_appDataPath);

            var dbPath = Path.Combine(_appDataPath, "snippets.db");
            _connectionString = $"Data Source={dbPath}";
            InitializeTables();
        }

        private void InitializeTables()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AppSettings (
                        SettingKey TEXT PRIMARY KEY,
                        SettingValue TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );";
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupService: 初始化 AppSettings 失败");
            }
        }

        /// <summary>
        /// 读取 WebDAV 设置
        /// </summary>
        public WebDavSettings GetWebDavSettings()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                return new WebDavSettings
                {
                    Url = GetSettingValue(connection, WebDavUrlKey, string.Empty),
                    Username = GetSettingValue(connection, WebDavUsernameKey, string.Empty),
                    Password = GetSettingValue(connection, WebDavPasswordKey, string.Empty),
                    IsAutoSyncEnabled = GetSettingValue(connection, WebDavAutoSyncKey, "false") == "true",
                    Directory = GetSettingValue(connection, WebDavDirectoryKey, string.Empty)
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupService: 读取 WebDAV 设置失败，返回默认值");
                return new WebDavSettings();
            }
        }

        /// <summary>
        /// 保存 WebDAV 设置
        /// </summary>
        public void SaveWebDavSettings(WebDavSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                SaveSettingValue(connection, WebDavUrlKey, settings.Url);
                SaveSettingValue(connection, WebDavUsernameKey, settings.Username);
                SaveSettingValue(connection, WebDavPasswordKey, settings.Password);
                SaveSettingValue(connection, WebDavAutoSyncKey, settings.IsAutoSyncEnabled ? "true" : "false");
                SaveSettingValue(connection, WebDavDirectoryKey, settings.Directory);
                Log.Information("BackupService: 已保存 WebDAV 设置");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupService: 保存 WebDAV 设置失败");
                throw;
            }
        }

        /// <summary>
        /// 导出备份到本地指定 zip 路径
        /// </summary>
        public void CreateBackupZip(string tempZipPath)
        {
            Log.Information("BackupService: 正在打包备份配置...");
            var tempDir = Path.Combine(_appDataPath, "TempBackup_" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
            Directory.CreateDirectory(tempDir);

            try
            {
                // 1. 使用 SQLite 在线备份 API，确保读取处于一致状态的 snippets.db
                var tempDbPath = Path.Combine(tempDir, "snippets.db");
                using (var source = new SqliteConnection(_connectionString))
                {
                    source.Open();
                    using var destination = new SqliteConnection($"Data Source={tempDbPath};Pooling=False");
                    destination.Open();
                    source.BackupDatabase(destination);
                }

                // 2. 拷贝 Scripts 目录（如果存在）
                var scriptsDir = Path.Combine(_appDataPath, "Scripts");
                if (Directory.Exists(scriptsDir))
                {
                    var destScriptsDir = Path.Combine(tempDir, "Scripts");
                    Directory.CreateDirectory(destScriptsDir);
                    CopyDirectory(scriptsDir, destScriptsDir);
                }

                // 3. 压缩整个 Temp 文件夹
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
                
                var zipParent = Path.GetDirectoryName(tempZipPath);
                if (!string.IsNullOrEmpty(zipParent))
                {
                    Directory.CreateDirectory(zipParent);
                }

                ZipFile.CreateFromDirectory(tempDir, tempZipPath, CompressionLevel.Optimal, false);
                Log.Information("BackupService: 备份包制作成功，路径: {Path}", tempZipPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupService: 制作配置备份失败");
                throw;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "BackupService: 清理临时备份目录失败");
                }
            }
        }

        /// <summary>
        /// 从本地指定 zip 路径恢复备份
        /// </summary>
        public void RestoreFromZip(string zipFilePath)
        {
            Log.Information("BackupService: 正在从备份包恢复配置: {Path}", zipFilePath);
            if (!File.Exists(zipFilePath))
            {
                throw new FileNotFoundException("备份文件不存在", zipFilePath);
            }

            var tempDir = Path.Combine(_appDataPath, "TempRestore_" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
            Directory.CreateDirectory(tempDir);

            try
            {
                // 1. 解压备份包
                ZipFile.ExtractToDirectory(zipFilePath, tempDir);

                // 2. 校验 snippets.db 是否存在
                var tempDbPath = Path.Combine(tempDir, "snippets.db");
                if (!File.Exists(tempDbPath))
                {
                    throw new FileNotFoundException("备份包无效：未找到 snippets.db，备份可能已损坏。");
                }

                // 3. 清理数据库连接池并强制垃圾回收以释放文件锁
                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // 4. 覆盖 active 数据库文件
                var activeDbPath = Path.Combine(_appDataPath, "snippets.db");
                
                // 确保有写权限，这里直接覆盖
                File.Copy(tempDbPath, activeDbPath, true);
                Log.Information("BackupService: 主数据库 snippets.db 已成功覆盖");

                // 5. 还原 Scripts 托管目录
                var tempScriptsDir = Path.Combine(tempDir, "Scripts");
                var activeScriptsDir = Path.Combine(_appDataPath, "Scripts");

                if (Directory.Exists(tempScriptsDir))
                {
                    if (Directory.Exists(activeScriptsDir))
                    {
                        Directory.Delete(activeScriptsDir, true);
                    }
                    Directory.CreateDirectory(activeScriptsDir);
                    CopyDirectory(tempScriptsDir, activeScriptsDir);
                    Log.Information("BackupService: 本地托管脚本目录已还原完成");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupService: 还原备份失败");
                throw;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "BackupService: 清理临时还原目录失败");
                }
            }
        }

        private string GetFullWebDavUrl(string baseUrl, string directory)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return string.Empty;
            var fullUrl = baseUrl.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(directory))
            {
                var trimmedDir = directory.Trim('/');
                if (!string.IsNullOrEmpty(trimmedDir))
                {
                    fullUrl = $"{fullUrl}/{trimmedDir}";
                }
            }
            return fullUrl + "/";
        }

        private async Task EnsureRemoteDirectoryStructureExistsAsync(HttpClient client, string baseUrl, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;

            var parts = directory.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var currentUrl = baseUrl.TrimEnd('/');

            foreach (var part in parts)
            {
                currentUrl = $"{currentUrl}/{part}";
                var checkUrl = currentUrl + "/";
                if (!await RemoteDirectoryExistsAsync(client, checkUrl))
                {
                    await CreateRemoteDirectoryAsync(client, checkUrl);
                }
            }
        }

        /// <summary>
        /// 测试 WebDAV 连接
        /// </summary>
        public async Task<bool> TestWebDavConnectionAsync(string url, string username, string password, string directory = "")
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            var targetUrl = GetFullWebDavUrl(url, directory);
            using var client = GetWebDavClient(username, password);
            try
            {
                // 使用 PROPFIND 方法测试连接，Depth 设为 0
                var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), targetUrl);
                request.Headers.Add("Depth", "0");
                
                var response = await client.SendAsync(request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Log.Warning("BackupService: WebDAV 连接测试失败：未授权 (401)");
                    return false;
                }

                var success = response.IsSuccessStatusCode || 
                              response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                              response.StatusCode == System.Net.HttpStatusCode.MultiStatus;
                
                Log.Information("BackupService: WebDAV 连接测试结果 = {Success} (状态码: {StatusCode})", success, response.StatusCode);
                return success;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupService: WebDAV 连接测试异常");
                return false;
            }
        }

        /// <summary>
        /// 手动上传备份至云端 WebDAV
        /// </summary>
        public async Task UploadBackupToWebDavAsync(string url, string username, string password, string directory, string localZipPath)
        {
            Log.Information("BackupService: 正在将备份上传至 WebDAV...");
            using var client = GetWebDavClient(username, password);

            // 1. 确保服务器上目标目录存在
            if (!string.IsNullOrWhiteSpace(directory))
            {
                await EnsureRemoteDirectoryStructureExistsAsync(client, url, directory);
            }
            else
            {
                if (!await RemoteDirectoryExistsAsync(client, url))
                {
                    await CreateRemoteDirectoryAsync(client, url);
                }
            }

            // 2. 上传备份文件
            var targetUrl = GetFullWebDavUrl(url, directory);
            var fileUrl = targetUrl + "toolbox_backup.zip";
            using var fileStream = File.OpenRead(localZipPath);
            using var content = new StreamContent(fileStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

            var response = await client.PutAsync(fileUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"上传备份到 WebDAV 失败，服务器返回状态码: {response.StatusCode}");
            }
            Log.Information("BackupService: 上传备份到 WebDAV 完成");
        }

        /// <summary>
        /// 手动从云端 WebDAV 下载备份
        /// </summary>
        public async Task DownloadBackupFromWebDavAsync(string url, string username, string password, string directory, string localZipPath)
        {
            Log.Information("BackupService: 正在从 WebDAV 下载备份...");
            using var client = GetWebDavClient(username, password);
            var targetUrl = GetFullWebDavUrl(url, directory);
            var fileUrl = targetUrl + "toolbox_backup.zip";

            var response = await client.GetAsync(fileUrl);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException("云端未找到备份文件 toolbox_backup.zip");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"从 WebDAV 下载备份失败，服务器返回状态码: {response.StatusCode}");
            }

            var dir = Path.GetDirectoryName(localZipPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var fileStream = File.Create(localZipPath);
            await response.Content.CopyToAsync(fileStream);
            Log.Information("BackupService: 从 WebDAV 下载备份完成");
        }

        /// <summary>
        /// 获取云端备份文件的最近修改时间
        /// </summary>
        public async Task<DateTime?> GetRemoteBackupLastModifiedAsync(string url, string username, string password, string directory = "")
        {
            using var client = GetWebDavClient(username, password);
            var targetUrl = GetFullWebDavUrl(url, directory);
            var fileUrl = targetUrl + "toolbox_backup.zip";

            try
            {
                // 发送 HEAD 请求获取 HTTP 头部信息
                var request = new HttpRequestMessage(HttpMethod.Head, fileUrl);
                var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode && response.Content.Headers.LastModified.HasValue)
                {
                    return response.Content.Headers.LastModified.Value.LocalDateTime;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BackupService: 获取 WebDAV 远程备份最近修改时间失败");
            }
            return null;
        }

        /// <summary>
        /// 智能双向配置同步
        /// </summary>
        public async Task<(SyncResult Result, string Message)> SyncWithWebDavAsync(string url, string username, string password, string directory = "")
        {
            Log.Information("BackupService: 开始执行 WebDAV 配置同步...");
            var activeDbPath = Path.Combine(_appDataPath, "snippets.db");
            if (!File.Exists(activeDbPath))
            {
                return (SyncResult.Failed, "本地配置数据库不存在，无法同步。");
            }

            var localTime = File.GetLastWriteTime(activeDbPath);
            var remoteTime = await GetRemoteBackupLastModifiedAsync(url, username, password, directory);

            var tempZipPath = Path.Combine(_appDataPath, "TempSyncBackup.zip");

            try
            {
                if (remoteTime == null)
                {
                    Log.Information("BackupService: 云端无备份文件，开始上传本地配置...");
                    CreateBackupZip(tempZipPath);
                    await UploadBackupToWebDavAsync(url, username, password, directory, tempZipPath);
                    return (SyncResult.Uploaded, "云端无备份，已成功将本地配置备份至云端。");
                }

                // 允许 2 秒的误差窗口（由于文件系统时间戳存储精度差异）
                if (localTime > remoteTime.Value.AddSeconds(2))
                {
                    Log.Information("BackupService: 本地配置较新 ({LocalTime}) > 远程配置 ({RemoteTime})，上传本地配置...", localTime, remoteTime.Value);
                    CreateBackupZip(tempZipPath);
                    await UploadBackupToWebDavAsync(url, username, password, directory, tempZipPath);
                    return (SyncResult.Uploaded, $"本地配置较新（本地: {localTime:yyyy-MM-dd HH:mm:ss}，云端: {remoteTime.Value:yyyy-MM-dd HH:mm:ss}），已更新云端备份。");
                }
                else if (remoteTime.Value > localTime.AddSeconds(2))
                {
                    Log.Information("BackupService: 云端配置较新 ({RemoteTime}) > 本地配置 ({LocalTime})，拉取并恢复...", remoteTime.Value, localTime);
                    await DownloadBackupFromWebDavAsync(url, username, password, directory, tempZipPath);
                    RestoreFromZip(tempZipPath);
                    return (SyncResult.Downloaded, $"云端配置较新（云端: {remoteTime.Value:yyyy-MM-dd HH:mm:ss}，本地: {localTime:yyyy-MM-dd HH:mm:ss}），已从云端拉取最新配置恢复。应用即将重启以应用更改。");
                }
                else
                {
                    Log.Information("BackupService: 本地配置与云端时间戳一致 ({LocalTime})，无须同步", localTime);
                    return (SyncResult.AlreadySynced, "本地配置与云端已是最新同步状态，无须重复操作。");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupService: 同步配置发生异常");
                return (SyncResult.Failed, $"同步失败：{ex.Message}");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempZipPath))
                    {
                        File.Delete(tempZipPath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "BackupService: 清理临时同步压缩包失败");
                }
            }
        }

        private HttpClient GetWebDavClient(string username, string password)
        {
            var client = new HttpClient();
            var byteArray = Encoding.UTF8.GetBytes($"{username}:{password}");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ToolBox/1.0");
            client.Timeout = TimeSpan.FromSeconds(15);
            return client;
        }

        private async Task<bool> RemoteDirectoryExistsAsync(HttpClient client, string url)
        {
            try
            {
                var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url.TrimEnd('/') + "/");
                request.Headers.Add("Depth", "0");
                var response = await client.SendAsync(request);
                return response.StatusCode == System.Net.HttpStatusCode.MultiStatus || 
                       response.StatusCode == System.Net.HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        private async Task CreateRemoteDirectoryAsync(HttpClient client, string url)
        {
            var request = new HttpRequestMessage(new HttpMethod("MKCOL"), url.TrimEnd('/') + "/");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.MethodNotAllowed)
            {
                throw new Exception($"无法在 WebDAV 服务器上创建备份目录 '{url}'。服务器响应状态码: {response.StatusCode}");
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }

        private string GetSettingValue(SqliteConnection connection, string key, string defaultValue)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey = @key";
            command.Parameters.AddWithValue("@key", key);
            var result = command.ExecuteScalar();
            return result is string val ? val : defaultValue;
        }

        private void SaveSettingValue(SqliteConnection connection, string key, string value)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO AppSettings (SettingKey, SettingValue, UpdatedAt) VALUES (@key, @value, @updatedAt)
                ON CONFLICT(SettingKey) DO UPDATE SET SettingValue = @value, UpdatedAt = @updatedAt";
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", value);
            command.Parameters.AddWithValue("@updatedAt", DateTime.Now.ToString("o"));
            command.ExecuteNonQuery();
        }
    }
}
