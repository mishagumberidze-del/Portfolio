using Microsoft.AspNetCore.Mvc;
using SpotifyTaste.Application;

namespace SpotifyTaste.Api.Controllers;

public sealed record ScoreRequest(string SpotifyUrlOrUri);

[ApiController]
[Route("api/scorer")]
public sealed class ScoringController(ISongScoringService scoringService) : ControllerBase
{
    [HttpPost]
    public Task<ScoreResult> Score([FromBody] ScoreRequest request, CancellationToken cancellationToken) => scoringService.ScoreAsync(request.SpotifyUrlOrUri, cancellationToken);
}

