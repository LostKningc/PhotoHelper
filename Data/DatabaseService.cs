using Microsoft.Data.Sqlite;
using PhotoHelper.Models;
using PhotoHelper.Utils;
using System.IO;

namespace PhotoHelper.Data;

public sealed class DatabaseService
{
    private const string DatabaseFileName = "photohelper.db";
    public const int CurrentSchemaVersion = 2;
    private readonly string _dbPath;
    private readonly string _connectionString;

    public DatabaseService(string dataDirectory, string? databaseFileName = null)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("Data directory must be provided.", nameof(dataDirectory));
        }

        Directory.CreateDirectory(dataDirectory);
        _dbPath = Path.Combine(dataDirectory, databaseFileName ?? DatabaseFileName);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public string DatabasePath => _dbPath;

    public int GetSchemaVersion()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS PhotoHistory (
                Uuid TEXT PRIMARY KEY,
                ImportTime TEXT NOT NULL,
                FileName TEXT NOT NULL,
                Size INTEGER NOT NULL,
                ModifiedTime TEXT NOT NULL,
                SourcePath TEXT NOT NULL,
                TargetPath TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_PhotoHistory_ImportTime ON PhotoHistory(ImportTime);
            CREATE INDEX IF NOT EXISTS IX_PhotoHistory_ModifiedTime ON PhotoHistory(ModifiedTime);
        ";
        command.ExecuteNonQuery();
    }

    public bool Exists(string uuid)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM PhotoHistory WHERE Uuid = $uuid LIMIT 1;";
        command.Parameters.AddWithValue("$uuid", uuid);

        using var reader = command.ExecuteReader();
        return reader.Read();
    }

    public HashSet<string> LoadAllUuids()
    {
        var results = new HashSet<string>(StringComparer.Ordinal);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Uuid FROM PhotoHistory;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                results.Add(reader.GetString(0));
            }
        }

        return results;
    }

    public void Insert(PhotoHistory record)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();
        InsertInternal(connection, record);
        transaction.Commit();
    }

    public void InsertBatch(IEnumerable<PhotoHistory> records)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();
        foreach (var record in records)
        {
            InsertInternal(connection, record);
        }
        transaction.Commit();
    }

    public void Clear()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PhotoHistory;";
        command.ExecuteNonQuery();
    }

    public sealed record UpgradeStats(int Upgraded, int SkippedMissingFile, int SkippedConflict)
    {
        public int Total => Upgraded + SkippedMissingFile + SkippedConflict;
    }

    public UpgradeStats UpgradeLegacyUuids(Action<string>? onSkipped = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var select = connection.CreateCommand();
        select.CommandText = @"
            SELECT Uuid, SourcePath, TargetPath
            FROM PhotoHistory
            WHERE (LENGTH(Uuid) - LENGTH(REPLACE(Uuid, '|', ''))) = 2;";

        using var transaction = connection.BeginTransaction();
        select.Transaction = transaction;
        using var reader = select.ExecuteReader();

        var updated = 0;
        var skippedMissing = 0;
        var skippedConflict = 0;
        while (reader.Read())
        {
            var legacyUuid = reader.GetString(0);
            var sourcePath = reader.GetString(1);
            var targetPath = reader.GetString(2);

            var path = File.Exists(targetPath) ? targetPath : File.Exists(sourcePath) ? sourcePath : null;
            if (path == null)
            {
                skippedMissing++;
                onSkipped?.Invoke($"旧记录缺少文件，跳过: {legacyUuid}");
                continue;
            }

            var info = new FileInfo(path);
            var newUuid = PhotoFileHelper.BuildUuid(info);
            var modified = info.LastWriteTimeUtc.ToString("O");

            using var update = connection.CreateCommand();
            update.CommandText = @"
                UPDATE OR IGNORE PhotoHistory
                SET Uuid = $newUuid,
                    ModifiedTime = $modifiedTime,
                    Size = $size,
                    FileName = $fileName
                WHERE Uuid = $oldUuid;";
            update.Transaction = transaction;
            update.Parameters.AddWithValue("$newUuid", newUuid);
            update.Parameters.AddWithValue("$modifiedTime", modified);
            update.Parameters.AddWithValue("$size", info.Length);
            update.Parameters.AddWithValue("$fileName", info.Name);
            update.Parameters.AddWithValue("$oldUuid", legacyUuid);

            var affected = update.ExecuteNonQuery();
            if (affected == 0)
            {
                skippedConflict++;
                onSkipped?.Invoke($"旧记录升级冲突，跳过: {legacyUuid}");
                continue;
            }

            updated++;
        }

        transaction.Commit();
        return new UpgradeStats(updated, skippedMissing, skippedConflict);
    }

    public UpgradeStats EnsureSchemaUpToDate(Action<string>? onSkipped = null)
    {
        var version = GetSchemaVersion();
        if (version >= CurrentSchemaVersion)
        {
            return new UpgradeStats(0, 0, 0);
        }

        var stats = new UpgradeStats(0, 0, 0);
        if (version < 2)
        {
            stats = UpgradeLegacyUuids(onSkipped);
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
        command.ExecuteNonQuery();

        return stats;
    }

    private static void InsertInternal(SqliteConnection connection, PhotoHistory record)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT OR IGNORE INTO PhotoHistory
            (Uuid, ImportTime, FileName, Size, ModifiedTime, SourcePath, TargetPath)
            VALUES
            ($uuid, $importTime, $fileName, $size, $modifiedTime, $sourcePath, $targetPath);";

        command.Parameters.AddWithValue("$uuid", record.Uuid);
        command.Parameters.AddWithValue("$importTime", record.ImportTime.ToString("O"));
        command.Parameters.AddWithValue("$fileName", record.FileName);
        command.Parameters.AddWithValue("$size", record.Size);
        command.Parameters.AddWithValue("$modifiedTime", record.ModifiedTime.ToString("O"));
        command.Parameters.AddWithValue("$sourcePath", record.SourcePath);
        command.Parameters.AddWithValue("$targetPath", record.TargetPath);

        command.ExecuteNonQuery();
    }
}
