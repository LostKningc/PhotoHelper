namespace PhotoHelper.Models;

public sealed record PhotoHistory
{
    public required string Uuid { get; init; }
    public required DateTime ImportTime { get; init; }
    public required string FileName { get; init; }
    public long Size { get; init; }
    public DateTime ModifiedTime { get; init; }
    public required string SourcePath { get; init; }
    public required string TargetPath { get; init; }
}
