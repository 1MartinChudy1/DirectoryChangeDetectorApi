namespace DirectoryChangeDetectorApi.Services;

public abstract class DirectoryAnalysisException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class InvalidDirectoryPathException(string message, Exception? innerException = null)
    : DirectoryAnalysisException(message, innerException);

public sealed class PathPointsToFileException(string message)
    : DirectoryAnalysisException(message);

public sealed class AnalysisAlreadyRunningException(string message)
    : DirectoryAnalysisException(message);
