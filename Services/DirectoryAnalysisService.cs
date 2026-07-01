using System.Security.Cryptography;
using DirectoryChangeDetectorApi.Models;

namespace DirectoryChangeDetectorApi.Services;

public sealed class DirectoryAnalysisService(
    ISnapshotStateStore stateStore,
    ILogger<DirectoryAnalysisService> logger) : IDirectoryAnalysisService
{
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        IgnoreInaccessible = false,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false
    };

    private readonly SemaphoreSlim _analysisLock = new(1, 1);

    public async Task<DirectoryAnalysisResponse> AnalyzeAsync(string path, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizePath(path);
        if (File.Exists(normalizedPath))
        {
            logger.LogWarning("Directory analysis rejected because path points to a file: {Path}", normalizedPath);
            throw new PathPointsToFileException($"Path '{normalizedPath}' points to a file. Provide a directory path.");
        }

        if (!Directory.Exists(normalizedPath))
        {
            logger.LogWarning("Directory analysis rejected because path does not exist: {Path}", normalizedPath);
            throw new DirectoryNotFoundException($"Directory '{normalizedPath}' does not exist.");
        }

        if (!await _analysisLock.WaitAsync(0, cancellationToken))
        {
            logger.LogWarning("Directory analysis rejected for {Path} because another analysis is already running.", normalizedPath);
            throw new AnalysisAlreadyRunningException("Another directory analysis is already running. Wait until it finishes and try again.");
        }

        try
        {
            logger.LogInformation("Directory analysis started for {Path}", normalizedPath);

            var state = await stateStore.LoadAsync(cancellationToken);
            var currentSnapshot = await ScanAsync(normalizedPath, cancellationToken);
            var isInitialRun = !state.Directories.TryGetValue(normalizedPath, out var previousSnapshot);

            var response = isInitialRun
                ? BuildInitialResponse(path, currentSnapshot)
                : BuildComparisonResponse(path, previousSnapshot!, currentSnapshot);

            state.Directories[normalizedPath] = currentSnapshot;
            await stateStore.SaveAsync(state, cancellationToken);

            logger.LogInformation(
                "Directory analysis completed for {Path}. Initial={Initial}; New={NewCount}; Changed={ChangedCount}; Removed={RemovedCount}",
                normalizedPath,
                response.IsInitialRun,
                response.NewEntries.Count,
                response.ChangedFiles.Count,
                response.RemovedEntries.Count);

            return response;
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Directory analysis failed because access was denied for {Path}", normalizedPath);
            throw;
        }
        catch (IOException exception)
        {
            logger.LogError(exception, "Directory analysis failed because an IO error occurred for {Path}", normalizedPath);
            throw;
        }
        finally
        {
            _analysisLock.Release();
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDirectoryPathException("Directory path is required.");
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDirectoryPathException($"Directory path '{path}' is not valid.", exception);
        }
    }

    private static async Task<DirectorySnapshot> ScanAsync(string rootPath, CancellationToken cancellationToken)
    {
        var snapshot = new DirectorySnapshot
        {
            RootPath = rootPath,
            AnalyzedAtUtc = DateTimeOffset.UtcNow
        };

        foreach (var directoryPath in Directory.EnumerateDirectories(rootPath, "*", EnumerationOptions).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.Directories.Add(GetRelativePath(rootPath, directoryPath));
        }

        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", EnumerationOptions).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = GetRelativePath(rootPath, filePath);
            var fileInfo = new FileInfo(filePath);

            snapshot.Files[relativePath] = new FileSnapshot
            {
                RelativePath = relativePath,
                Sha256 = await ComputeSha256Async(filePath, cancellationToken),
                SizeBytes = fileInfo.Length,
                Version = 1
            };
        }

        return snapshot;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static DirectoryAnalysisResponse BuildInitialResponse(string requestedPath, DirectorySnapshot currentSnapshot)
    {
        var newEntries = currentSnapshot.Directories
            .Select(relativePath => new EntryChange(relativePath, FileSystemEntryKind.Directory, null))
            .Concat(currentSnapshot.Files.Values
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .Select(file => new EntryChange(file.RelativePath, FileSystemEntryKind.File, file.Version)))
            .ToArray();

        return new DirectoryAnalysisResponse(
            requestedPath,
            currentSnapshot.RootPath,
            IsInitialRun: true,
            currentSnapshot.AnalyzedAtUtc,
            newEntries,
            ChangedFiles: [],
            RemovedEntries: [],
            BuildCurrentFiles(currentSnapshot));
    }

    private static DirectoryAnalysisResponse BuildComparisonResponse(
        string requestedPath,
        DirectorySnapshot previousSnapshot,
        DirectorySnapshot currentSnapshot)
    {
        var newEntries = new List<EntryChange>();
        var changedFiles = new List<EntryChange>();
        var removedEntries = new List<EntryChange>();

        foreach (var directory in currentSnapshot.Directories.Except(previousSnapshot.Directories, StringComparer.Ordinal))
        {
            newEntries.Add(new EntryChange(directory, FileSystemEntryKind.Directory, null));
        }

        foreach (var directory in previousSnapshot.Directories.Except(currentSnapshot.Directories, StringComparer.Ordinal))
        {
            removedEntries.Add(new EntryChange(directory, FileSystemEntryKind.Directory, null));
        }

        foreach (var currentFile in currentSnapshot.Files.Values.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            if (!previousSnapshot.Files.TryGetValue(currentFile.RelativePath, out var previousFile))
            {
                currentFile.Version = 1;
                newEntries.Add(new EntryChange(currentFile.RelativePath, FileSystemEntryKind.File, currentFile.Version));
                continue;
            }

            currentFile.Version = previousFile.Sha256 == currentFile.Sha256
                ? previousFile.Version
                : previousFile.Version + 1;

            if (currentFile.Version != previousFile.Version)
            {
                changedFiles.Add(new EntryChange(currentFile.RelativePath, FileSystemEntryKind.File, currentFile.Version));
            }
        }

        foreach (var previousFile in previousSnapshot.Files.Values.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            if (!currentSnapshot.Files.ContainsKey(previousFile.RelativePath))
            {
                removedEntries.Add(new EntryChange(previousFile.RelativePath, FileSystemEntryKind.File, previousFile.Version));
            }
        }

        return new DirectoryAnalysisResponse(
            requestedPath,
            currentSnapshot.RootPath,
            IsInitialRun: false,
            currentSnapshot.AnalyzedAtUtc,
            newEntries,
            changedFiles,
            removedEntries,
            BuildCurrentFiles(currentSnapshot));
    }

    private static CurrentFileVersion[] BuildCurrentFiles(DirectorySnapshot snapshot)
    {
        return snapshot.Files.Values
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .Select(file => new CurrentFileVersion(file.RelativePath, file.Version))
            .ToArray();
    }

    private static string GetRelativePath(string rootPath, string path)
    {
        return Path.GetRelativePath(rootPath, path).Replace(Path.DirectorySeparatorChar, '/');
    }
}
