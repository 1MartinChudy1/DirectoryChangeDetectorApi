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
}
