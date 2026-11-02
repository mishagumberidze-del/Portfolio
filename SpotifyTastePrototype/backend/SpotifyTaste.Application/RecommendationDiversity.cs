namespace SpotifyTaste.Application;

public sealed record RankedCandidate(string Identity, string ArtistIdentity, string? AlbumIdentity, double Score);

public static class RecommendationDiversity
{
    public static IReadOnlyList<RankedCandidate> Select(IEnumerable<RankedCandidate> candidates, int count, int maxPerArtist = 1, int maxPerAlbum = 1)
    {
        var selected = new List<RankedCandidate>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var artistCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var albumCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates.OrderByDescending(x => x.Score))
        {
            if (!identities.Add(candidate.Identity)) continue;
            artistCounts.TryGetValue(candidate.ArtistIdentity, out var artistCount);
            if (artistCount >= maxPerArtist) continue;
            if (candidate.AlbumIdentity is not null)
            {
                albumCounts.TryGetValue(candidate.AlbumIdentity, out var albumCount);
                if (albumCount >= maxPerAlbum) continue;
                albumCounts[candidate.AlbumIdentity] = albumCount + 1;
            }
            artistCounts[candidate.ArtistIdentity] = artistCount + 1;
            selected.Add(candidate);
            if (selected.Count == count) break;
        }
        return selected;
    }
}

public static class RecommendationEligibility
{
    public static bool IsNeverHeard(string spotifyTrackId, IReadOnlySet<string> historySpotifyIds) =>
        !historySpotifyIds.Contains(spotifyTrackId);

    public static bool IsNotSaved(string spotifyTrackId, IReadOnlySet<string> savedSpotifyIds) =>
        !savedSpotifyIds.Contains(spotifyTrackId);
}
