namespace DirectoryChangeDetectorApi.Models;

public enum FileSystemEntryKind
{
    File,
    Directory
}

public sealed record EntryChange(
    string RelativePath,
    FileSystemEntryKind Kind,
    int? Version);

public sealed record CurrentFileVersion(
    string RelativePath,
    int Version);

public sealed record DirectoryAnalysisResponse(
    string RequestedPath,
    string AnalyzedPath,
    bool IsInitialRun,
    DateTimeOffset AnalyzedAtUtc,
    IReadOnlyCollection<EntryChange> NewEntries,
    IReadOnlyCollection<EntryChange> ChangedFiles,
    IReadOnlyCollection<EntryChange> RemovedEntries,
    IReadOnlyCollection<CurrentFileVersion> CurrentFiles);
