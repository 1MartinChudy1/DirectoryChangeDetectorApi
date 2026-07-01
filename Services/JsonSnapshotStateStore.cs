using System.Text.Json;
using DirectoryChangeDetectorApi.Models;

namespace DirectoryChangeDetectorApi.Services;

public sealed class JsonSnapshotStateStore(IHostEnvironment environment, ILogger<JsonSnapshotStateStore> logger) : ISnapshotStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _statePath = Path.Combine(environment.ContentRootPath, ".data", "snapshots.json");

    public async Task<SnapshotState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath))
        {
            return new SnapshotState();
        }

        await using var stream = File.OpenRead(_statePath);
        var state = await JsonSerializer.DeserializeAsync<SnapshotState>(stream, JsonOptions, cancellationToken);

        return NormalizeState(state ?? new SnapshotState());
    }

    public async Task SaveAsync(SnapshotState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_statePath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, _statePath, overwrite: true);
        logger.LogInformation("Snapshot state saved to {StatePath}", _statePath);
    }

    private static SnapshotState NormalizeState(SnapshotState state)
    {
        state.Directories = new Dictionary<string, DirectorySnapshot>(state.Directories, StringComparer.Ordinal);

        foreach (var snapshot in state.Directories.Values)
        {
            snapshot.Files = new Dictionary<string, FileSnapshot>(snapshot.Files, StringComparer.Ordinal);
            snapshot.Directories = new SortedSet<string>(snapshot.Directories, StringComparer.Ordinal);
        }

        return state;
    }
}
