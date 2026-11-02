using Microsoft.AspNetCore.Mvc;
using SpotifyTaste.Application;

namespace SpotifyTaste.Api.Controllers;

[ApiController]
[Route("api/model")]
public sealed class ModelController(IPreferenceModelService preferenceModel, ITrackFeatureProvider featureProvider) : ControllerBase
{
    [HttpPost("rebuild")]
    public Task<PreferenceValidationDto> Rebuild(CancellationToken cancellationToken) => preferenceModel.RebuildAsync(cancellationToken);

    [HttpPost("enrich")]
    public async Task<object> Enrich([FromQuery] int limit = 100, CancellationToken cancellationToken = default) => new { completed = await featureProvider.EnrichMissingAsync(limit, cancellationToken) };
}

