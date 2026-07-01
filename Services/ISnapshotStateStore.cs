using DirectoryChangeDetectorApi.Models;

namespace DirectoryChangeDetectorApi.Services;

public interface ISnapshotStateStore
{
    Task<SnapshotState> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(SnapshotState state, CancellationToken cancellationToken);
}
