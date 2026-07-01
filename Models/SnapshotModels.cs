namespace DirectoryChangeDetectorApi.Models;

public sealed class SnapshotState
{
    public Dictionary<string, DirectorySnapshot> Directories { get; set; } = new(StringComparer.Ordinal);
}

public sealed class DirectorySnapshot
{
    public required string RootPath { get; set; }

    public DateTimeOffset AnalyzedAtUtc { get; set; }

    public Dictionary<string, FileSnapshot> Files { get; set; } = new(StringComparer.Ordinal);

    public SortedSet<string> Directories { get; set; } = new(StringComparer.Ordinal);
}

public sealed class FileSnapshot
{
    public required string RelativePath { get; set; }

    public required string Sha256 { get; set; }

    public long SizeBytes { get; set; }

    public int Version { get; set; }
}
