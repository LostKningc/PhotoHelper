using System.Globalization;
using System.IO;

namespace PhotoHelper.Utils;

public static class PhotoFileHelper
{
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
        var name = fileInfo.Name;
        var size = fileInfo.Length;
        var modified = fileInfo.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture);
        return $"{name}|{size}|{modified}";
    }
}
