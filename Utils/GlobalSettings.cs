using System.IO;
using System.Text.Json;

namespace PhotoHelper.Utils;

public sealed class GlobalSettings
{
    public string? LastTargetPath { get; set; }
    public List<string> RecentTargets { get; set; } = new();

    public static GlobalSettings Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new GlobalSettings();
        }

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<GlobalSettings>(json) ?? new GlobalSettings();
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
