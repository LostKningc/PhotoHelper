using PhotoHelper.Data;
using PhotoHelper.Logging;
using PhotoHelper.Models;
using PhotoHelper.Utils;
using System.IO;

namespace PhotoHelper.Services;

public sealed class ScanService
{
    private readonly DatabaseService _databaseService;
    private readonly Logger _logger;

    public ScanService(DatabaseService databaseService, Logger logger)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<PhotoHistory> ScanForNewPhotos(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path must be provided.", nameof(sourcePath));
        }

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Source path not found: {sourcePath}");
        }

        var results = new List<PhotoHistory>();
        var existingUuids = _databaseService.LoadAllUuids();
        var processed = 0;

        foreach (var file in SafeEnumerateFiles(sourcePath))
        {
            processed++;
            try
            {
                if (!PhotoFileHelper.IsSupportedPhoto(file))
                {
                    continue;
                }

                var fileInfo = new FileInfo(file);
                var uuid = PhotoFileHelper.BuildUuid(fileInfo);
                var legacyUuid = PhotoFileHelper.BuildLegacyUuid(fileInfo);

                if (existingUuids.Contains(uuid) || existingUuids.Contains(legacyUuid))
                {
                    continue;
                }

                results.Add(new PhotoHistory
                {
                    Uuid = uuid,
                    ImportTime = DateTime.Now,
                    FileName = fileInfo.Name,
                    Size = fileInfo.Length,
                    ModifiedTime = fileInfo.LastWriteTimeUtc,
                    SourcePath = fileInfo.FullName,
                    TargetPath = string.Empty
                });

                _logger.Info($"[待导入] {fileInfo.Name} ({processed})");
            }
            catch (Exception ex)
            {
                _logger.Warning($"跳过文件: {file}. 原因: {ex.Message}");
            }
        }

        _logger.Info($"扫描完成，发现新照片 {results.Count} 张。");
        return results;
    }

    private IEnumerable<string> SafeEnumerateFiles(string sourcePath)
    {
        var pending = new Stack<string>();
        pending.Push(sourcePath);

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
