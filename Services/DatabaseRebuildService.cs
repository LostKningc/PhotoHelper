using Microsoft.Data.Sqlite;
using PhotoHelper.Data;
using PhotoHelper.Logging;
using PhotoHelper.Models;
using PhotoHelper.Utils;
using System.IO;

namespace PhotoHelper.Services;

public sealed class DatabaseRebuildService
{
    private readonly DatabaseService _databaseService;
    private readonly Logger _logger;

    public DatabaseRebuildService(DatabaseService databaseService, Logger logger)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int Rebuild(string targetRoot)
    {
        if (string.IsNullOrWhiteSpace(targetRoot))
        {
            throw new ArgumentException("Target root must be provided.", nameof(targetRoot));
        }

        if (!Directory.Exists(targetRoot))
        {
            throw new DirectoryNotFoundException($"Target root not found: {targetRoot}");
        }

        _logger.Info("开始重建历史数据库...");
        var originalPath = _databaseService.DatabasePath;
        var dataDirectory = Path.GetDirectoryName(originalPath)
            ?? throw new InvalidOperationException("无法解析数据库目录。");
        var tempPath = Path.Combine(dataDirectory, "photohelper.rebuild.db");
        var backupPath = Path.Combine(dataDirectory, "photohelper.backup.db");

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        var tempDatabase = new DatabaseService(dataDirectory, Path.GetFileName(tempPath));
        tempDatabase.Initialize();

        var records = new List<PhotoHistory>();
        var processed = 0;

        try
        {
            foreach (var file in SafeEnumerateFiles(targetRoot))
            {
                processed++;
                try
                {
                    if (!PhotoFileHelper.IsSupportedPhoto(file))
                    {
                        continue;
                    }

                    var info = new FileInfo(file);
                    records.Add(new PhotoHistory
                    {
                        Uuid = PhotoFileHelper.BuildUuid(info),
                        ImportTime = DateTime.Now,
                        FileName = info.Name,
                        Size = info.Length,
                        ModifiedTime = info.LastWriteTimeUtc,
                        SourcePath = info.FullName,
                        TargetPath = info.FullName
                    });

                    if (records.Count >= 500)
                    {
                        tempDatabase.InsertBatch(records);
                        records.Clear();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"重建时跳过文件: {file}. 原因: {ex.Message}");
                }
            }

            if (records.Count > 0)
            {
                tempDatabase.InsertBatch(records);
            }

            SqliteConnection.ClearAllPools();

            if (File.Exists(originalPath))
            {
                File.Replace(tempPath, originalPath, backupPath, true);
                _logger.Info($"数据库已替换，备份文件: {backupPath}");
            }
            else
            {
                File.Move(tempPath, originalPath);
            }
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }

        _logger.Info($"数据库重建完成。扫描文件 {processed} 个。");
        return processed;
    }

    private IEnumerable<string> SafeEnumerateFiles(string targetRoot)
    {
        var pending = new Stack<string>();
        pending.Push(targetRoot);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> directories;
            IEnumerable<string> files;

            try
            {
                directories = Directory.EnumerateDirectories(current).ToArray();
                files = Directory.EnumerateFiles(current).ToArray();
            }
            catch (Exception ex)
            {
                _logger.Warning($"无法访问目录: {current}. {ex.Message}");
                continue;
            }

            foreach (var directory in directories)
            {
                pending.Push(directory);
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }
}
