using DirectoryChangeDetectorApi.Models;
using DirectoryChangeDetectorApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace DirectoryChangeDetectorApi.Controllers;

[ApiController]
[Route("api/directory-analysis")]
public sealed class DirectoryAnalysisController(IDirectoryAnalysisService analysisService) : ControllerBase
{
    /// <summary>
    /// Manually analyzes a local directory and compares it with the last stored snapshot.
    /// </summary>
    /// <param name="path">Absolute or relative path to a directory on the server filesystem.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    [HttpPost("analyze")]
    [ProducesResponseType(typeof(DirectoryAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DirectoryAnalysisResponse>> Analyze(
        [FromQuery] string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await analysisService.AnalyzeAsync(path, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid directory path.",
                Detail = exception.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (DirectoryNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Directory not found.",
                Detail = exception.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Directory is not accessible.",
                Detail = exception.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
        catch (IOException exception)
        {
            return Problem(
                title: "Directory analysis failed.",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
