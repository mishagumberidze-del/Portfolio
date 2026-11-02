using Microsoft.AspNetCore.Mvc;
using SpotifyTaste.Application;

namespace SpotifyTaste.Api.Controllers;

[ApiController]
[Route("api/import")]
public sealed class ImportController(ISpotifyImportService importService) : ControllerBase
{
    [HttpPost("spotify")]
    public Task<ImportReport> ImportSpotify([FromBody] ImportRequest request, CancellationToken cancellationToken) => importService.ImportAsync(request.SourcePath, cancellationToken);
}

