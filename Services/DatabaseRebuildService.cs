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
        _databaseService.Clear();

        var records = new List<PhotoHistory>();
        var files = SafeEnumerateFiles(targetRoot);
        var processed = 0;

        foreach (var file in files)
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
                    ModifiedTime = info.LastWriteTime,
                    SourcePath = info.FullName,
                    TargetPath = info.FullName
                });

                if (records.Count >= 500)
                {
                    _databaseService.InsertBatch(records);
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
            _databaseService.InsertBatch(records);
        }

        _logger.Info($"数据库重建完成。扫描文件 {processed} 个。");
        return processed;
    }

    private List<string> SafeEnumerateFiles(string targetRoot)
    {
        var results = new List<string>();
        var pending = new Stack<string>();
        pending.Push(targetRoot);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    pending.Push(directory);
                }

                foreach (var file in Directory.EnumerateFiles(current))
                {
                    results.Add(file);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"无法访问目录: {current}. {ex.Message}");
            }
        }

        return results;
    }
}
