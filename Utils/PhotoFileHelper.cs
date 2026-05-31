using System.Globalization;
using System.IO;
using System.Security.Cryptography;

namespace PhotoHelper.Utils;

public static class PhotoFileHelper
{
    private const int HashSegmentSize = 1024 * 64; // 64KB
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".arw", ".cr3", ".nef", ".dng", ".orf", ".rw2"
    };

    public static bool IsSupportedPhoto(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension);
    }

    public static string BuildUuid(FileInfo fileInfo)
    {
        var legacy = BuildLegacyUuid(fileInfo);
        var hash = ComputeQuickHash(fileInfo.FullName, fileInfo.Length);
        return $"{legacy}|{hash}";
    }

    public static string BuildLegacyUuid(FileInfo fileInfo)
    {
        var name = fileInfo.Name;
        var size = fileInfo.Length;
        var modified = fileInfo.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture);
        return $"{name}|{size}|{modified}";
    }

    private static string ComputeQuickHash(string path, long length)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (length <= HashSegmentSize * 2)
        {
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        var buffer = new byte[HashSegmentSize];
        var read = stream.Read(buffer, 0, buffer.Length);
        sha.TransformBlock(buffer, 0, read, null, 0);

        stream.Seek(-HashSegmentSize, SeekOrigin.End);
        read = stream.Read(buffer, 0, buffer.Length);
        sha.TransformFinalBlock(buffer, 0, read);

        return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>());
    }
}
