using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SpotifyTaste.Application;

namespace SpotifyTaste.Infrastructure.ReccoBeats;

public sealed class ReccoBeatsClient(HttpClient httpClient) : IReccoBeatsClient
{
    public async Task<ExternalTrack?> GetBySpotifyIdAsync(string spotifyTrackId, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRetryAsync($"v1/track?ids={Uri.EscapeDataString(spotifyTrackId)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<ReccoPage<ReccoTrack>>(cancellationToken: cancellationToken);
        var track = page?.Content.FirstOrDefault();
        if (track is null) return null;

        using var featureResponse = await SendWithRetryAsync($"v1/track/{track.Id}/audio-features", cancellationToken);
        ReccoFeatures? features = null;
        if (featureResponse.IsSuccessStatusCode) features = await featureResponse.Content.ReadFromJsonAsync<ReccoFeatures>(cancellationToken: cancellationToken);
        else if (featureResponse.StatusCode != HttpStatusCode.NotFound) featureResponse.EnsureSuccessStatusCode();
        return Map(track, features);
    }

    public async Task<IReadOnlyList<ExternalTrack>> GetRecommendationsAsync(IReadOnlyList<string> positiveProviderIds, IReadOnlyList<string> negativeProviderIds, int limit, CancellationToken cancellationToken = default)
    {
        if (positiveProviderIds.Count == 0) return [];
        var desired = Math.Clamp(limit, 1, 1000);
        var results = new Dictionary<string, ExternalTrack>(StringComparer.OrdinalIgnoreCase);
        var batches = Math.Max(1, (int)Math.Ceiling(desired / 100d));
        for (var batch = 0; batch < batches && results.Count < desired; batch++)
        {
            var positiveSeeds = RotateTake(positiveProviderIds, batch * 5, 5);
            var negativeSeeds = RotateTake(negativeProviderIds.Where(x => !positiveSeeds.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray(), batch * 5, 5);
            var size = Math.Min(100, desired - results.Count);
            var query = $"v1/track/recommendation?seeds={Uri.EscapeDataString(string.Join(',', positiveSeeds))}&size={size}";
            if (negativeSeeds.Count > 0) query += $"&negativeSeeds={Uri.EscapeDataString(string.Join(',', negativeSeeds))}";
            using var response = await SendWithRetryAsync(query, cancellationToken);
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<ReccoPage<ReccoTrack>>(cancellationToken: cancellationToken);
            var before = results.Count;
            foreach (var track in page?.Content ?? [])
            {
                var mapped = Map(track, null);
                if (mapped.SpotifyTrackId is not null) results.TryAdd(mapped.SpotifyTrackId, mapped);
            }
            if (results.Count == before && batch > 0) break;
        }
        return results.Values.Take(desired).ToList();
    }

    private static IReadOnlyList<string> RotateTake(IReadOnlyList<string> source, int offset, int count)
    {
        if (source.Count == 0) return [];
        var result = new List<string>(Math.Min(count, source.Count));
        for (var index = 0; index < Math.Min(count, source.Count); index++) result.Add(source[(offset + index) % source.Count]);
        return result;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string requestUri, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var response = await httpClient.GetAsync(requestUri, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == 4) return response;
            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
            response.Dispose();
            await Task.Delay(delay > TimeSpan.FromMinutes(2) ? TimeSpan.FromMinutes(2) : delay, cancellationToken);
        }
        throw new InvalidOperationException("Unreachable retry state.");
    }

    private static ExternalTrack Map(ReccoTrack track, ReccoFeatures? features)
    {
        var spotifyId = TryGetSpotifyId(track.Href);
        return new ExternalTrack(
            track.Id,
            spotifyId,
            track.TrackTitle,
            track.Artists.DistinctBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Name).ToList(),
            null,
            track.DurationMs,
            track.Isrc,
            track.Href,
            null,
            features is null ? null : new TrackFeatureVector(features.Tempo, features.Energy, features.Danceability, features.Valence, features.Acousticness, features.Instrumentalness, features.Speechiness, features.Liveness, features.Loudness, features.Key, features.Mode, features.TimeSignature));
    }

    private static string? TryGetSpotifyId(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        try { return SpotifyTrackIdParser.Parse(href); }
        catch (ArgumentException) { return null; }
    }
}

public sealed record ReccoPage<T>([property: JsonPropertyName("content")] IReadOnlyList<T> Content);
public sealed record ReccoArtist([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("href")] string? Href);
public sealed record ReccoTrack(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("trackTitle")] string TrackTitle,
    [property: JsonPropertyName("artists")] IReadOnlyList<ReccoArtist> Artists,
    [property: JsonPropertyName("durationMs")] int? DurationMs,
    [property: JsonPropertyName("isrc")] string? Isrc,
    [property: JsonPropertyName("href")] string? Href);
public sealed record ReccoFeatures(
    [property: JsonPropertyName("acousticness")] double? Acousticness,
    [property: JsonPropertyName("danceability")] double? Danceability,
    [property: JsonPropertyName("energy")] double? Energy,
    [property: JsonPropertyName("instrumentalness")] double? Instrumentalness,
    [property: JsonPropertyName("key")] short? Key,
    [property: JsonPropertyName("liveness")] double? Liveness,
    [property: JsonPropertyName("loudness")] double? Loudness,
    [property: JsonPropertyName("mode")] short? Mode,
    [property: JsonPropertyName("speechiness")] double? Speechiness,
    [property: JsonPropertyName("tempo")] double? Tempo,
    [property: JsonPropertyName("valence")] double? Valence,
    [property: JsonPropertyName("timeSignature")] short? TimeSignature);
