using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpotifyTaste.Application;
using SpotifyTaste.Infrastructure.Persistence;

namespace SpotifyTaste.Infrastructure.ReccoBeats;

public sealed class ReccoBeatsCandidateTrackProvider(SpotifyTasteDbContext dbContext, IReccoBeatsClient client, IOptions<PreferenceModelOptions>? configuredOptions = null) : ICandidateTrackProvider
{
    private readonly PreferenceModelOptions options = configuredOptions?.Value ?? new PreferenceModelOptions();
    public string Name => "ReccoBeats";

    public async Task<IReadOnlyList<ExternalTrack>> GetCandidatesAsync(int desiredCount, CancellationToken cancellationToken = default)
    {
        var mapped = dbContext.TrackExternalIds.Where(x => x.Provider == "ReccoBeats");
        var preferences = dbContext.TrackPreferences.Where(x => x.ModelVersion == options.ModelVersion);
        // SQL DISTINCT discards a preceding ORDER BY. Deduplicate first, then
        // rank, so the provider really receives strongest/weakest taste seeds.
        var mappedPreferences = preferences.Join(mapped, p => p.TrackId, e => e.TrackId, (p, e) => new { e.ExternalId, p.BehavioralScore });
        var positive = await mappedPreferences.GroupBy(x => x.ExternalId).Select(g => new { ExternalId = g.Key, Score = g.Max(x => x.BehavioralScore) })
            .OrderByDescending(x => x.Score).ThenBy(x => x.ExternalId).Take(20).Select(x => x.ExternalId).ToListAsync(cancellationToken);
        var negative = await mappedPreferences.GroupBy(x => x.ExternalId).Select(g => new { ExternalId = g.Key, Score = g.Min(x => x.BehavioralScore) })
            .OrderBy(x => x.Score).ThenBy(x => x.ExternalId).Take(20).Select(x => x.ExternalId).ToListAsync(cancellationToken);
        negative = negative.Except(positive, StringComparer.OrdinalIgnoreCase).ToList();
        if (positive.Count == 0)
        {
            positive = await dbContext.ListeningEvents.GroupBy(x => x.TrackId).Select(g => new { TrackId = g.Key, Ms = g.Sum(e => (long)e.MsPlayed) })
                .Join(mapped, p => p.TrackId, e => e.TrackId, (p, e) => new { e.ExternalId, p.Ms })
                .GroupBy(x => x.ExternalId).Select(g => new { ExternalId = g.Key, Ms = g.Sum(x => x.Ms) })
                .OrderByDescending(x => x.Ms).ThenBy(x => x.ExternalId).Take(20).Select(x => x.ExternalId).ToListAsync(cancellationToken);
        }
        return await client.GetRecommendationsAsync(positive, negative, Math.Clamp(desiredCount, 1, 1000), cancellationToken);
    }
}
