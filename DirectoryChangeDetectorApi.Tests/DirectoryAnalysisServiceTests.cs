using DirectoryChangeDetectorApi.Models;
using DirectoryChangeDetectorApi.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DirectoryChangeDetectorApi.Tests;

public sealed class DirectoryAnalysisServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"directory-change-detector-{Guid.NewGuid():N}");
    private readonly string _stateRoot;
    private readonly string _watchedRoot;
    private readonly DirectoryAnalysisService _service;

    public DirectoryAnalysisServiceTests()
    {
        _stateRoot = Path.Combine(_tempRoot, "state");
        _watchedRoot = Path.Combine(_tempRoot, "watched");

        Directory.CreateDirectory(_stateRoot);
        Directory.CreateDirectory(_watchedRoot);

        var stateStore = new JsonSnapshotStateStore(
            new TestHostEnvironment(_stateRoot),
            NullLogger<JsonSnapshotStateStore>.Instance);

        _service = new DirectoryAnalysisService(
            stateStore,
            NullLogger<DirectoryAnalysisService>.Instance);
    }

    [Fact]
    public async Task AnalyzeAsync_FirstRun_ReturnsBaselineAsNewEntries()
    {
        Directory.CreateDirectory(Path.Combine(_watchedRoot, "docs"));
        await File.WriteAllTextAsync(Path.Combine(_watchedRoot, "readme.txt"), "initial");
        await File.WriteAllTextAsync(Path.Combine(_watchedRoot, "docs", "manual.txt"), "manual");

        var result = await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        Assert.True(result.IsInitialRun);
        Assert.Empty(result.ChangedFiles);
        Assert.Empty(result.RemovedEntries);
        Assert.Contains(result.NewEntries, entry => entry is { RelativePath: "docs", Kind: FileSystemEntryKind.Directory, Version: null });
        Assert.Contains(result.NewEntries, entry => entry is { RelativePath: "readme.txt", Kind: FileSystemEntryKind.File, Version: 1 });
        Assert.Contains(result.NewEntries, entry => entry is { RelativePath: "docs/manual.txt", Kind: FileSystemEntryKind.File, Version: 1 });
        Assert.All(result.CurrentFiles, file => Assert.Equal(1, file.Version));
    }

    [Fact]
    public async Task AnalyzeAsync_SubsequentRun_ReturnsNewChangedAndRemovedEntries()
    {
        Directory.CreateDirectory(Path.Combine(_watchedRoot, "docs"));
        Directory.CreateDirectory(Path.Combine(_watchedRoot, "old-folder"));
        await File.WriteAllTextAsync(Path.Combine(_watchedRoot, "changed.txt"), "v1");
        await File.WriteAllTextAsync(Path.Combine(_watchedRoot, "unchanged.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(_watchedRoot, "removed.txt"), "remove me");

        await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        File.Delete(Path.Combine(_watchedRoot, "removed.txt"));
        Directory.Delete(Path.Combine(_watchedRoot, "old-folder"));
        await File.WriteAllTextAsync(Path.Combine(_watchedRoot, "changed.txt"), "v2");
        await File.WriteAllTextAsync(Path.Combine(_watchedRoot, "new.txt"), "new file");
        Directory.CreateDirectory(Path.Combine(_watchedRoot, "new-folder"));

        var result = await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        Assert.False(result.IsInitialRun);
        Assert.Contains(result.NewEntries, entry => entry is { RelativePath: "new.txt", Kind: FileSystemEntryKind.File, Version: 1 });
        Assert.Contains(result.NewEntries, entry => entry is { RelativePath: "new-folder", Kind: FileSystemEntryKind.Directory, Version: null });
        Assert.Contains(result.ChangedFiles, entry => entry is { RelativePath: "changed.txt", Kind: FileSystemEntryKind.File, Version: 2 });
        Assert.Contains(result.RemovedEntries, entry => entry is { RelativePath: "removed.txt", Kind: FileSystemEntryKind.File, Version: 1 });
        Assert.Contains(result.RemovedEntries, entry => entry is { RelativePath: "old-folder", Kind: FileSystemEntryKind.Directory, Version: null });
        Assert.Contains(result.CurrentFiles, file => file is { RelativePath: "unchanged.txt", Version: 1 });
        Assert.Contains(result.CurrentFiles, file => file is { RelativePath: "changed.txt", Version: 2 });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnalyzeAsync_BlankPath_ThrowsInvalidDirectoryPathException(string path)
    {
        await Assert.ThrowsAsync<InvalidDirectoryPathException>(() => _service.AnalyzeAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task AnalyzeAsync_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var missingPath = Path.Combine(_tempRoot, "does-not-exist");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _service.AnalyzeAsync(missingPath, CancellationToken.None));
    }

    [Fact]
    public async Task AnalyzeAsync_PathToFile_ThrowsPathPointsToFileException()
    {
        var filePath = Path.Combine(_tempRoot, "not-a-directory.txt");
        await File.WriteAllTextAsync(filePath, "content");

        await Assert.ThrowsAsync<PathPointsToFileException>(() => _service.AnalyzeAsync(filePath, CancellationToken.None));
    }

    [Fact]
    public async Task AnalyzeAsync_UnchangedSecondRun_ReturnsNoChangesAndKeepsVersions()
    {
        await File.WriteAllTextAsync(Path.Combine(_watchedRoot, "stable.txt"), "same");

        await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        var result = await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        Assert.False(result.IsInitialRun);
        Assert.Empty(result.NewEntries);
        Assert.Empty(result.ChangedFiles);
        Assert.Empty(result.RemovedEntries);
        var currentFile = Assert.Single(result.CurrentFiles);
        Assert.Equal("stable.txt", currentFile.RelativePath);
        Assert.Equal(1, currentFile.Version);
    }

    [Fact]
    public async Task AnalyzeAsync_SameSizeDifferentContent_IncrementsVersion()
    {
        var filePath = Path.Combine(_watchedRoot, "same-size.txt");
        await File.WriteAllTextAsync(filePath, "abcd");

        await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        await File.WriteAllTextAsync(filePath, "wxyz");

        var result = await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        var changedFile = Assert.Single(result.ChangedFiles);
        Assert.Equal("same-size.txt", changedFile.RelativePath);
        Assert.Equal(FileSystemEntryKind.File, changedFile.Kind);
        Assert.Equal(2, changedFile.Version);
    }

    [Fact]
    public async Task AnalyzeAsync_RemovedDirectoryWithNestedFile_ReportsBothRemoved()
    {
        var nestedDirectory = Path.Combine(_watchedRoot, "removed-folder");
        Directory.CreateDirectory(nestedDirectory);
        await File.WriteAllTextAsync(Path.Combine(nestedDirectory, "nested.txt"), "content");

        await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        Directory.Delete(nestedDirectory, recursive: true);

        var result = await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        Assert.Contains(result.RemovedEntries, entry => entry is { RelativePath: "removed-folder", Kind: FileSystemEntryKind.Directory, Version: null });
        Assert.Contains(result.RemovedEntries, entry => entry is { RelativePath: "removed-folder/nested.txt", Kind: FileSystemEntryKind.File, Version: 1 });
    }

    [Fact]
    public async Task AnalyzeAsync_RenamedFile_IsReportedAsRemovedAndNew()
    {
        var originalPath = Path.Combine(_watchedRoot, "original.txt");
        var renamedPath = Path.Combine(_watchedRoot, "renamed.txt");
        await File.WriteAllTextAsync(originalPath, "content");

        await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        File.Move(originalPath, renamedPath);

        var result = await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        Assert.Empty(result.ChangedFiles);
        Assert.Contains(result.RemovedEntries, entry => entry is { RelativePath: "original.txt", Kind: FileSystemEntryKind.File, Version: 1 });
        Assert.Contains(result.NewEntries, entry => entry is { RelativePath: "renamed.txt", Kind: FileSystemEntryKind.File, Version: 1 });
    }

    [Fact]
    public async Task AnalyzeAsync_FileReplacedByDirectoryAtSameRelativePath_ReportsRemovedFileAndNewDirectory()
    {
        var sharedPath = Path.Combine(_watchedRoot, "item");
        await File.WriteAllTextAsync(sharedPath, "file");

        await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        File.Delete(sharedPath);
        Directory.CreateDirectory(sharedPath);
        await File.WriteAllTextAsync(Path.Combine(sharedPath, "nested.txt"), "nested");

        var result = await _service.AnalyzeAsync(_watchedRoot, CancellationToken.None);

        Assert.Contains(result.RemovedEntries, entry => entry is { RelativePath: "item", Kind: FileSystemEntryKind.File, Version: 1 });
        Assert.Contains(result.NewEntries, entry => entry is { RelativePath: "item", Kind: FileSystemEntryKind.Directory, Version: null });
        Assert.Contains(result.NewEntries, entry => entry is { RelativePath: "item/nested.txt", Kind: FileSystemEntryKind.File, Version: 1 });
    }

    [Fact]
    public async Task AnalyzeAsync_WhenAnotherAnalysisIsRunning_ThrowsAnalysisAlreadyRunningException()
    {
        await File.WriteAllTextAsync(Path.Combine(_watchedRoot, "file.txt"), "content");
        var blockingStore = new BlockingSnapshotStateStore();
        var service = new DirectoryAnalysisService(
            blockingStore,
            NullLogger<DirectoryAnalysisService>.Instance);

        var firstAnalysis = service.AnalyzeAsync(_watchedRoot, CancellationToken.None);
        await blockingStore.WaitUntilLoadStartedAsync();

        await Assert.ThrowsAsync<AnalysisAlreadyRunningException>(() => service.AnalyzeAsync(_watchedRoot, CancellationToken.None));

        blockingStore.ReleaseLoad();
        await firstAnalysis;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Tests";

        public string ApplicationName { get; set; } = "DirectoryChangeDetectorApi.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class BlockingSnapshotStateStore : ISnapshotStateStore
    {
        private readonly TaskCompletionSource _loadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SnapshotState> LoadAsync(CancellationToken cancellationToken)
        {
            _loadStarted.SetResult();
            await _releaseLoad.Task.WaitAsync(cancellationToken);

            return new SnapshotState();
        }

        public Task SaveAsync(SnapshotState state, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task WaitUntilLoadStartedAsync()
        {
            return _loadStarted.Task;
        }

        public void ReleaseLoad()
        {
            _releaseLoad.SetResult();
        }
    }
}
