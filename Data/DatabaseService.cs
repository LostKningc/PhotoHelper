using Microsoft.Data.Sqlite;
using PhotoHelper.Models;
using System.IO;

namespace PhotoHelper.Data;

public sealed class DatabaseService
{
    private const string DatabaseFileName = "photohelper.db";
    private readonly string _connectionString;

    public DatabaseService(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("Data directory must be provided.", nameof(dataDirectory));
        }

        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, DatabaseFileName);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
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
            );";
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
