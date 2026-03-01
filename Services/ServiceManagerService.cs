using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace ToolBox.Services
{
    /// <summary>
    /// Windows 服务信息
    /// </summary>
    public class ServiceInfo
    {
        public string ServiceName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Status { get; set; } = "";
        public string StartType { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsFavorite { get; set; }
    }

    /// <summary>
    /// 服务分组（场景化批量启停）
    /// </summary>
    public class ServiceGroup
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public List<string> ServiceNames { get; set; } = new();
    }

    /// <summary>
    /// Windows 服务管理服务
    /// </summary>
    public class ServiceManagerService
    {
        private readonly string _connectionString;

        public ServiceManagerService()
        {
            // 复用 ToolBox 数据库
            var appDataPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolBox");
            System.IO.Directory.CreateDirectory(appDataPath);
            var dbPath = System.IO.Path.Combine(appDataPath, "snippets.db");
            _connectionString = $"Data Source={dbPath}";
            InitializeTables();
        }

        private void InitializeTables()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS FavoriteServices (
                    ServiceName TEXT PRIMARY KEY
                );

                CREATE TABLE IF NOT EXISTS ServiceGroups (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE
                );

                CREATE TABLE IF NOT EXISTS ServiceGroupItems (
                    GroupId INTEGER NOT NULL,
                    ServiceName TEXT NOT NULL,
                    PRIMARY KEY (GroupId, ServiceName),
                    FOREIGN KEY (GroupId) REFERENCES ServiceGroups(Id) ON DELETE CASCADE
                );
            ";
            cmd.ExecuteNonQuery();
        }

        // ========== 服务查询 ==========

        /// <summary>
        /// 获取所有 Windows 服务信息
        /// </summary>
        public List<ServiceInfo> GetAllServices()
        {
            var favorites = GetFavoriteServiceNames();
            var services = new List<ServiceInfo>();

            foreach (var sc in ServiceController.GetServices())
            {
                try
                {
                    services.Add(new ServiceInfo
                    {
                        ServiceName = sc.ServiceName,
                        DisplayName = sc.DisplayName,
                        Status = sc.Status.ToString(),
                        StartType = GetStartType(sc),
                        Description = "",
                        IsFavorite = favorites.Contains(sc.ServiceName)
                    });
                }
                catch
                {
                    // 跳过无法访问的服务
                }
                finally
                {
                    sc.Dispose();
                }
            }

            return services.OrderBy(s => s.DisplayName).ToList();
        }

        private static string GetStartType(ServiceController sc)
        {
            try
            {
                return sc.StartType switch
                {
                    ServiceStartMode.Automatic => "自动",
                    ServiceStartMode.Manual => "手动",
                    ServiceStartMode.Disabled => "禁用",
                    ServiceStartMode.Boot => "Boot",
                    ServiceStartMode.System => "System",
                    _ => "未知"
                };
            }
            catch
            {
                return "未知";
            }
        }

        // ========== 服务操作 ==========

        /// <summary>
        /// 启动服务
        /// </summary>
        public async Task<(bool success, string message)> StartServiceAsync(string serviceName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var sc = new ServiceController(serviceName);
                    if (sc.Status == ServiceControllerStatus.Running)
                        return (true, "服务已在运行中");

                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    return (true, $"已启动: {sc.DisplayName}");
                }
                catch (Exception ex)
                {
                    return (false, $"启动失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public async Task<(bool success, string message)> StopServiceAsync(string serviceName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var sc = new ServiceController(serviceName);
                    if (sc.Status == ServiceControllerStatus.Stopped)
                        return (true, "服务已停止");

                    if (!sc.CanStop)
                        return (false, "该服务不允许停止");

                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    return (true, $"已停止: {sc.DisplayName}");
                }
                catch (Exception ex)
                {
                    return (false, $"停止失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 重启服务
        /// </summary>
        public async Task<(bool success, string message)> RestartServiceAsync(string serviceName)
        {
            var stopResult = await StopServiceAsync(serviceName);
            if (!stopResult.success && !stopResult.message.Contains("已停止"))
                return stopResult;

            return await StartServiceAsync(serviceName);
        }

        /// <summary>
        /// 获取服务实时状态
        /// </summary>
        public string GetServiceStatus(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                sc.Refresh();
                return sc.Status.ToString();
            }
            catch
            {
                return "Unknown";
            }
        }

        // ========== 收藏 ==========

        public HashSet<string> GetFavoriteServiceNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ServiceName FROM FavoriteServices";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
            return names;
        }

        public void ToggleFavorite(string serviceName)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM FavoriteServices WHERE ServiceName = $name";
            checkCmd.Parameters.AddWithValue("$name", serviceName);
            var count = (long)checkCmd.ExecuteScalar()!;

            var cmd = conn.CreateCommand();
            if (count > 0)
            {
                cmd.CommandText = "DELETE FROM FavoriteServices WHERE ServiceName = $name";
            }
            else
            {
                cmd.CommandText = "INSERT INTO FavoriteServices (ServiceName) VALUES ($name)";
            }
            cmd.Parameters.AddWithValue("$name", serviceName);
            cmd.ExecuteNonQuery();
        }

        // ========== 服务分组 ==========

        public List<ServiceGroup> GetAllGroups()
        {
            var groups = new List<ServiceGroup>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name FROM ServiceGroups ORDER BY Name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var group = new ServiceGroup
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1)
                };

                // 加载组内服务
                var itemCmd = conn.CreateCommand();
                itemCmd.CommandText = "SELECT ServiceName FROM ServiceGroupItems WHERE GroupId = $gid";
                itemCmd.Parameters.AddWithValue("$gid", group.Id);
                using var itemReader = itemCmd.ExecuteReader();
                while (itemReader.Read())
                {
                    group.ServiceNames.Add(itemReader.GetString(0));
                }

                groups.Add(group);
            }
            return groups;
        }

        public long CreateGroup(string name, List<string> serviceNames)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ServiceGroups (Name) VALUES ($name);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$name", name);
            var groupId = (long)cmd.ExecuteScalar()!;

            foreach (var sn in serviceNames)
            {
                var itemCmd = conn.CreateCommand();
                itemCmd.CommandText = "INSERT INTO ServiceGroupItems (GroupId, ServiceName) VALUES ($gid, $sn)";
                itemCmd.Parameters.AddWithValue("$gid", groupId);
                itemCmd.Parameters.AddWithValue("$sn", sn);
                itemCmd.ExecuteNonQuery();
            }

            return groupId;
        }

        public void DeleteGroup(long groupId)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var cmd1 = conn.CreateCommand();
            cmd1.CommandText = "DELETE FROM ServiceGroupItems WHERE GroupId = $gid";
            cmd1.Parameters.AddWithValue("$gid", groupId);
            cmd1.ExecuteNonQuery();

            var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "DELETE FROM ServiceGroups WHERE Id = $gid";
            cmd2.Parameters.AddWithValue("$gid", groupId);
            cmd2.ExecuteNonQuery();
        }

        /// <summary>
        /// 批量启动分组内所有服务
        /// </summary>
        public async Task<List<(string service, bool success, string message)>> StartGroupAsync(long groupId)
        {
            var group = GetAllGroups().FirstOrDefault(g => g.Id == groupId);
            if (group == null) return new();

            var results = new List<(string, bool, string)>();
            foreach (var sn in group.ServiceNames)
            {
                var (success, msg) = await StartServiceAsync(sn);
                results.Add((sn, success, msg));
            }
            return results;
        }

        /// <summary>
        /// 批量停止分组内所有服务
        /// </summary>
        public async Task<List<(string service, bool success, string message)>> StopGroupAsync(long groupId)
        {
            var group = GetAllGroups().FirstOrDefault(g => g.Id == groupId);
            if (group == null) return new();

            var results = new List<(string, bool, string)>();
            foreach (var sn in group.ServiceNames)
            {
                var (success, msg) = await StopServiceAsync(sn);
                results.Add((sn, success, msg));
            }
            return results;
        }
    }
}
