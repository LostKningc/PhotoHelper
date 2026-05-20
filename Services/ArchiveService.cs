using PhotoHelper.Logging;
using PhotoHelper.Models;
using PhotoHelper.Utils;
using System.IO;

namespace PhotoHelper.Services;

public sealed class ArchiveService
{
    private const int BufferSize = 1024 * 1024 * 4; // 4MB
    private readonly Logger _logger;

    public ArchiveService(Logger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PhotoHistory ArchivePhoto(PhotoHistory record, string targetRoot)
    {
        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        if (string.IsNullOrWhiteSpace(targetRoot))
        {
            throw new ArgumentException("Target root must be provided.", nameof(targetRoot));
        }

        var captureTime = MetadataHelper.ResolveCaptureTime(record.SourcePath);
        var targetDirectory = Path.Combine(
            targetRoot,
            captureTime.ToString("yyyy"),
            captureTime.ToString("MM"),
            captureTime.ToString("dd"));

        Directory.CreateDirectory(targetDirectory);

        var (targetPath, alreadyExists) = BuildSafeTargetPath(targetDirectory, record.FileName, record.SourcePath);

        if (!alreadyExists)
        {
            CopyFile(record.SourcePath, targetPath);
            _logger.Info($"[成功] 复制 {record.FileName} 至 {targetDirectory}");
        }
        else
        {
            _logger.Info($"[跳过] 目标已存在相同文件 {record.FileName}");
        }

        return record with
        {
            TargetPath = targetPath,
            ImportTime = DateTime.Now
        };
    }

    private static (string Path, bool AlreadyExists) BuildSafeTargetPath(
        string directory,
        string fileName,
        string sourcePath)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        var counter = 1;

        while (File.Exists(candidate))
        {
            if (IsSameFile(sourcePath, candidate))
            {
                return (candidate, true);
            }

            candidate = Path.Combine(directory, $"{baseName}_{counter}{extension}");
            counter++;
        }

        return (candidate, false);
    }

    private static bool IsSameFile(string sourcePath, string targetPath)
    {
        var source = new FileInfo(sourcePath);
        var target = new FileInfo(targetPath);

        return source.Length == target.Length && source.LastWriteTimeUtc == target.LastWriteTimeUtc;
    }

    private static void CopyFile(string sourcePath, string targetPath)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(target, BufferSize);
    }
}
