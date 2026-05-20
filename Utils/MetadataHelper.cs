using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System.IO;
using System.Linq;

namespace PhotoHelper.Utils;

public static class MetadataHelper
{
    public static DateTime ResolveCaptureTime(string filePath)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();

            if (subIfd != null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var original))
            {
                return original;
            }

            if (subIfd != null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var digitized))
            {
                return digitized;
            }

            if (ifd0 != null && ifd0.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var dateTime))
            {
                return dateTime;
            }
        }
        catch
        {
            // ignored: fall back to file timestamps
        }

        var info = new FileInfo(filePath);
        return info.CreationTime <= info.LastWriteTime ? info.CreationTime : info.LastWriteTime;
    }
}
