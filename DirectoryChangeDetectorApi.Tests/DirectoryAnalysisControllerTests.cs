using DirectoryChangeDetectorApi.Controllers;
using DirectoryChangeDetectorApi.Models;
using DirectoryChangeDetectorApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace DirectoryChangeDetectorApi.Tests;

public sealed class DirectoryAnalysisControllerTests
{
    [Fact]
    public async Task Analyze_WhenServiceReturnsResponse_ReturnsOk()
    {
        var response = new DirectoryAnalysisResponse(
            RequestedPath: "/tmp",
            AnalyzedPath: "/tmp",
            IsInitialRun: true,
            AnalyzedAtUtc: DateTimeOffset.UtcNow,
            NewEntries: [],
            ChangedFiles: [],
            RemovedEntries: [],
            CurrentFiles: []);

        var controller = CreateController(new StubAnalysisService((_, _) => Task.FromResult(response)));

        var result = await controller.Analyze("/tmp", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(response, okResult.Value);
    }

    [Fact]
    public async Task Analyze_WhenPathIsInvalid_ReturnsBadRequest()
    {
        var controller = CreateController(new StubAnalysisService((_, _) => throw new InvalidDirectoryPathException("Directory path is required.")));

        var result = await controller.Analyze("", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Invalid directory path.", problem.Title);
    }

    [Fact]
    public async Task Analyze_WhenPathPointsToFile_ReturnsBadRequest()
    {
        var controller = CreateController(new StubAnalysisService((_, _) => throw new PathPointsToFileException("Path points to a file.")));

        var result = await controller.Analyze("/tmp/file.txt", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Path points to a file.", problem.Title);
    }

    [Fact]
    public async Task Analyze_WhenDirectoryDoesNotExist_ReturnsNotFound()
    {
        var controller = CreateController(new StubAnalysisService((_, _) => throw new DirectoryNotFoundException("missing")));

        var result = await controller.Analyze("/missing", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("Directory not found.", problem.Title);
    }

    [Fact]
    public async Task Analyze_WhenAnotherAnalysisIsRunning_ReturnsConflict()
    {
        var controller = CreateController(new StubAnalysisService((_, _) => throw new AnalysisAlreadyRunningException("Wait until it finishes and try again.")));

        var result = await controller.Analyze("/tmp", CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("Directory analysis is already running.", problem.Title);
        Assert.Contains("Wait", problem.Detail);
    }

    [Fact]
    public async Task Analyze_WhenDirectoryIsNotAccessible_ReturnsForbidden()
    {
        var controller = CreateController(new StubAnalysisService((_, _) => throw new UnauthorizedAccessException("denied")));

        var result = await controller.Analyze("/denied", CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(forbidden.Value);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        Assert.Equal("Directory is not accessible.", problem.Title);
    }

    [Fact]
    public async Task Analyze_WhenFileReadFails_ReturnsServerError()
    {
        var controller = CreateController(new StubAnalysisService((_, _) => throw new IOException("read failed")));

        var result = await controller.Analyze("/io-error", CancellationToken.None);

        var serverError = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(serverError.Value);
        Assert.Equal(StatusCodes.Status500InternalServerError, serverError.StatusCode);
        Assert.Equal("Directory analysis failed.", problem.Title);
    }

    [Fact]
    public async Task Analyze_WhenUnexpectedErrorOccurs_ReturnsGenericServerError()
    {
        var controller = CreateController(new StubAnalysisService((_, _) => throw new InvalidOperationException("implementation detail")));

        var result = await controller.Analyze("/unexpected", CancellationToken.None);

        var serverError = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(serverError.Value);
        Assert.Equal(StatusCodes.Status500InternalServerError, serverError.StatusCode);
        Assert.Equal("Unexpected directory analysis error.", problem.Title);
        Assert.DoesNotContain("implementation detail", problem.Detail);
    }

    private static DirectoryAnalysisController CreateController(IDirectoryAnalysisService analysisService)
    {
        return new DirectoryAnalysisController(
            analysisService,
            NullLogger<DirectoryAnalysisController>.Instance);
    }

    private sealed class StubAnalysisService(Func<string, CancellationToken, Task<DirectoryAnalysisResponse>> analyze) : IDirectoryAnalysisService
    {
        public Task<DirectoryAnalysisResponse> AnalyzeAsync(string path, CancellationToken cancellationToken)
        {
            return analyze(path, cancellationToken);
        }
    }
}
