using DirectoryChangeDetectorApi.Models;

namespace DirectoryChangeDetectorApi.Services;

public interface IDirectoryAnalysisService
{
    Task<DirectoryAnalysisResponse> AnalyzeAsync(string path, CancellationToken cancellationToken);
}
