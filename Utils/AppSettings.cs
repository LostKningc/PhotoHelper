using System.IO;
using System.Text.Json;

namespace PhotoHelper.Utils;

public sealed class AppSettings
{
    public string? SourcePath { get; set; }
    public string? TargetPath { get; set; }

    public static AppSettings Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public void Save(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(filePath, json);
    }
}
