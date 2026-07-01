using DirectoryChangeDetectorApi.Models;
using DirectoryChangeDetectorApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace DirectoryChangeDetectorApi.Controllers;

[ApiController]
[Route("api/directory-analysis")]
public sealed class DirectoryAnalysisController(
    IDirectoryAnalysisService analysisService,
    ILogger<DirectoryAnalysisController> logger) : ControllerBase
{
    /// <summary>
    /// Manually analyzes a local directory and compares it with the last stored snapshot.
    /// </summary>
    /// <param name="path">Absolute or relative path to a directory on the server filesystem.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    [HttpPost("analyze")]
    [ProducesResponseType(typeof(DirectoryAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DirectoryAnalysisResponse>> Analyze(
        [FromQuery] string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await analysisService.AnalyzeAsync(path, cancellationToken));
        }
        catch (InvalidDirectoryPathException exception)
        {
            logger.LogWarning(exception, "Directory analysis request rejected because the path is invalid.");
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid directory path.",
                Detail = exception.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (PathPointsToFileException exception)
        {
            logger.LogWarning(exception, "Directory analysis request rejected because the path points to a file.");
            return BadRequest(new ProblemDetails
            {
                Title = "Path points to a file.",
                Detail = exception.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (DirectoryNotFoundException exception)
        {
            logger.LogWarning(exception, "Directory analysis request rejected because the directory was not found.");
            return NotFound(new ProblemDetails
            {
                Title = "Directory not found.",
                Detail = exception.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (AnalysisAlreadyRunningException exception)
        {
            logger.LogWarning(exception, "Directory analysis request rejected because another analysis is already running.");
            return Conflict(new ProblemDetails
            {
                Title = "Directory analysis is already running.",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Directory analysis request rejected because the directory is not accessible.");
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Directory is not accessible.",
                Detail = exception.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
        catch (IOException exception)
        {
            logger.LogError(exception, "Directory analysis failed because of an IO error.");
            return Problem(
                title: "Directory analysis failed.",
                detail: $"The directory could not be analyzed because a filesystem IO error occurred: {exception.Message}",
                statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Directory analysis failed unexpectedly.");
            return Problem(
                title: "Unexpected directory analysis error.",
                detail: "The directory could not be analyzed because an unexpected server error occurred.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
