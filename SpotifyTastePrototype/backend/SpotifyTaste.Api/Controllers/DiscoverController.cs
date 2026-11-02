using Microsoft.AspNetCore.Mvc;
using SpotifyTaste.Application;
using SpotifyTaste.Infrastructure.Recommendations;

namespace SpotifyTaste.Api.Controllers;

[ApiController]
[Route("api/discover")]
public sealed class DiscoverController(IRecommendationService recommendationService, FeedbackService feedback, RecommendationWorkGate gate) : ControllerBase
{
    [HttpGet]
    public Task<DiscoverDto> Latest(CancellationToken cancellationToken) => feedback.LatestAsync(cancellationToken);
    [HttpPost("generate")]
    public async Task<DiscoverDto> Generate([FromQuery] int count = 10, CancellationToken cancellationToken = default)
    {
        await gate.Gate.WaitAsync(cancellationToken);
        try { return await recommendationService.GenerateAsync(count, cancellationToken); }
        finally { gate.Gate.Release(); }
    }
}
