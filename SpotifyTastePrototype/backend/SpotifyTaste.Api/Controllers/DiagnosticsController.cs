using Microsoft.AspNetCore.Mvc;
using SpotifyTaste.Application;

namespace SpotifyTaste.Api.Controllers;

[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController(IDiagnosticsService diagnosticsService) : ControllerBase
{
    [HttpGet]
    public Task<DiagnosticsDto> Get(CancellationToken cancellationToken) => diagnosticsService.GetAsync(cancellationToken);
}
