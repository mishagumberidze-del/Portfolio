using Microsoft.AspNetCore.Mvc;
using SpotifyTaste.Application;

namespace SpotifyTaste.Api.Controllers;

[ApiController]
[Route("api/analytics")]
public sealed class AnalyticsController(IAnalyticsService analyticsService) : ControllerBase
{
    [HttpGet("overview")]
    public Task<OverviewDto> Overview([FromQuery] string range = "lifetime", [FromQuery] int? year = null, CancellationToken cancellationToken = default) { var bounds = Bounds(range, year); return analyticsService.GetOverviewAsync(bounds.From, bounds.To, cancellationToken); }

    [HttpGet("top-tracks")]
    public Task<IReadOnlyList<TopTrackDto>> TopTracks([FromQuery] string range = "lifetime", [FromQuery] int? year = null, [FromQuery] int limit = 50, CancellationToken cancellationToken = default) { var bounds = Bounds(range, year); return analyticsService.GetTopTracksAsync(bounds.From, bounds.To, limit, cancellationToken); }

    [HttpGet("top-artists")]
    public Task<IReadOnlyList<TopArtistDto>> TopArtists([FromQuery] string range = "lifetime", [FromQuery] int? year = null, [FromQuery] int limit = 50, CancellationToken cancellationToken = default) { var bounds = Bounds(range, year); return analyticsService.GetTopArtistsAsync(bounds.From, bounds.To, limit, cancellationToken); }

    [HttpGet("top-albums")]
    public Task<IReadOnlyList<TopAlbumDto>> TopAlbums([FromQuery] string range = "lifetime", [FromQuery] int? year = null, [FromQuery] int limit = 50, CancellationToken cancellationToken = default) { var bounds = Bounds(range, year); return analyticsService.GetTopAlbumsAsync(bounds.From, bounds.To, limit, cancellationToken); }

    [HttpGet("timeline")]
    public Task<IReadOnlyList<TimelinePointDto>> Timeline(CancellationToken cancellationToken) => analyticsService.GetTimelineAsync(cancellationToken);

    [HttpGet("timeline/yearly")]
    public Task<IReadOnlyList<TimelinePointDto>> YearlyTimeline(CancellationToken cancellationToken) => analyticsService.GetYearlyTimelineAsync(cancellationToken);

    [HttpGet("artist-evolution")]
    public Task<IReadOnlyList<RankEvolutionDto>> ArtistEvolution([FromQuery] string grain = "year", CancellationToken cancellationToken = default) => analyticsService.GetArtistEvolutionAsync(grain, cancellationToken);

    [HttpGet("behavior")]
    public Task<BehaviorAnalyticsDto> Behavior([FromQuery] string range = "lifetime", [FromQuery] int? year = null, CancellationToken cancellationToken = default) { var bounds = Bounds(range, year); return analyticsService.GetBehaviorAsync(bounds.From, bounds.To, cancellationToken); }

    private static (DateTime? From, DateTime? To) Bounds(string range, int? year)
    {
        if (year is < 2000 or > 2100) throw new ArgumentOutOfRangeException(nameof(year), "Year must be between 2000 and 2100.");
        if (year.HasValue)
        {
            var from = new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (from, from.AddYears(1));
        }
        return range.ToLowerInvariant() switch
        {
            "last365" => (DateTime.UtcNow.AddDays(-365), null),
            "last90" => (DateTime.UtcNow.AddDays(-90), null),
            "last30" => (DateTime.UtcNow.AddDays(-30), null),
            _ => (null, null),
        };
    }
}
