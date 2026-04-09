using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ToolBox.Services
{
    /// <summary>
    /// 收藏端口的持久化服务。
    /// </summary>
    public class PortFavoriteService
    {
        private readonly string _connectionString;

        public PortFavoriteService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolBox");
            Directory.CreateDirectory(appDataPath);
            var dbPath = Path.Combine(appDataPath, "snippets.db");
            _connectionString = $"Data Source={dbPath}";
            InitializeTable();
        }

        private void InitializeTable()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS FavoritePorts (
                    Port INTEGER PRIMARY KEY
                );";
            command.ExecuteNonQuery();
        }

        public HashSet<int> GetFavoritePorts()
        {
            var ports = new HashSet<int>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Port FROM FavoritePorts";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ports.Add(reader.GetInt32(0));
            }

            return ports;
        }

        public void ToggleFavorite(int port)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "SELECT COUNT(*) FROM FavoritePorts WHERE Port = $port";
            checkCommand.Parameters.AddWithValue("$port", port);
            var exists = (long)checkCommand.ExecuteScalar()! > 0;

            var command = connection.CreateCommand();
            command.CommandText = exists
                ? "DELETE FROM FavoritePorts WHERE Port = $port"
                : "INSERT INTO FavoritePorts (Port) VALUES ($port)";
            command.Parameters.AddWithValue("$port", port);
            command.ExecuteNonQuery();
        }
    }
}