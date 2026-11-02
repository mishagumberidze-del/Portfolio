using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SpotifyTaste.Application;

namespace SpotifyTaste.Infrastructure.Spotify;

public sealed record SpotifySavedArtist(string Id, string Name);
public sealed record SpotifySavedSong(string Id, string Name, DateTime? SavedAtUtc, int? DurationMs, string? Isrc,
    string? AlbumId, string? AlbumName, IReadOnlyList<SpotifySavedArtist> Artists);
public sealed record SpotifyLibrarySnapshot(int Total, IReadOnlyList<SpotifySavedSong> Songs);
public interface ISpotifyLibraryClient { Task<SpotifyLibrarySnapshot> ReadAllAsync(CancellationToken ct); }

public sealed class SpotifyLibraryClient(HttpClient http, SpotifyConnection connection) : ISpotifyLibraryClient
{
    public Task<SpotifyLibrarySnapshot> ReadAllAsync(CancellationToken ct) => ReadPagesAsync(GetPageAsync, ct);

    private async Task<JsonDocument> GetPageAsync(int offset, CancellationToken ct)
    {
        var refreshed = false;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.spotify.com/v1/me/tracks?limit=50&offset={offset}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await connection.AccessTokenAsync(ct));
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
            {
                await connection.AccessTokenAsync(ct, forceRefresh: true); refreshed = true; continue;
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(5);
                if (wait > TimeSpan.FromSeconds(30) || attempt == 3)
                    throw new SpotifyConnectionException("Spotify is rate-limiting this app. Try again later; the previous SQL library is unchanged.");
                await Task.Delay(wait < TimeSpan.Zero ? TimeSpan.FromSeconds(1) : wait, ct); continue;
            }
            if ((int)response.StatusCode >= 500 && attempt < 3) { await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct); continue; }
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new SpotifyConnectionException("Spotify refused library access (403). Check the app's allowed users, the app owner's Premium subscription, and user-library-read permission.");
            if (!response.IsSuccessStatusCode) throw new SpotifyConnectionException($"Spotify library request failed (HTTP {(int)response.StatusCode}). No partial library will be applied.");
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        throw new SpotifyConnectionException("Spotify could not complete the library request. Reconnect or try later.");
    }

    // Never follow provider-controlled next URLs with a bearer token. Use bounded offsets
    // on the fixed Spotify endpoint and verify counts, identity uniqueness and page shape.
    public static async Task<SpotifyLibrarySnapshot> ReadPagesAsync(Func<int, CancellationToken, Task<JsonDocument>> getPage, CancellationToken ct)
    {
        var songs = new List<SpotifySavedSong>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int? total = null;
        for (var offset = 0; offset <= 100_000; offset += 50)
        {
            using var page = await getPage(offset, ct);
            var root = page.RootElement;
            var count = Integer(root, "total");
            if (count is null or < 0 or > 100_000 || Integer(root, "offset") != offset || (total is not null && count != total))
                throw Invalid("The library changed during download or its pagination was invalid. Retry without editing Liked Songs.");
            total ??= count;
            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != Math.Min(50, total.Value - offset))
                throw Invalid("A Spotify library page was incomplete. The previous snapshot is unchanged.");
            foreach (var item in items.EnumerateArray())
            {
                var song = ParseSong(item);
                if (!ids.Add(song.Id)) throw Invalid("A song repeated across library pages; the library may have changed. Please retry.");
                songs.Add(song);
            }
            if (!root.TryGetProperty("next", out var next)) throw Invalid("Spotify omitted library pagination information.");
            if (songs.Count == total)
            {
                if (next.ValueKind != JsonValueKind.Null) throw Invalid("Spotify returned inconsistent library pagination.");
                return new(total.Value, songs);
            }
            if (next.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(next.GetString())) throw Invalid("Spotify stopped pagination before all songs were received.");
        }
        throw Invalid("Library exceeds the safety limit; no partial snapshot was imported.");
    }

    public static SpotifySavedSong ParseSong(JsonElement item)
    {
        if (!item.TryGetProperty("track", out var t) || t.ValueKind != JsonValueKind.Object || (t.TryGetProperty("is_local", out var local) && local.ValueKind == JsonValueKind.True))
            throw Invalid("A saved song was unavailable or local-only. No partial snapshot was applied; use a library export or resolve the unavailable item.");
        var id = Text(t, "id");
        // Relinked playback IDs must not replace the original saved identity.
        if (t.TryGetProperty("linked_from", out var linked) && Text(linked, "id") is { } original) id = original;
        if (!ValidId(id)) throw Invalid("A saved track had no valid Spotify identity.");
        var name = RequiredText(t, "name", 500);
        if (!t.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array || artists.GetArrayLength() == 0)
            throw Invalid("A saved track had no artist metadata.");
        var artistRows = artists.EnumerateArray().Select(a => new SpotifySavedArtist(RequiredId(a, "id"), RequiredText(a, "name", 300))).DistinctBy(a => a.Id).ToArray();
        if (artistRows.Length > 100) throw Invalid("A saved track exceeded the artist safety limit.");
        string? albumId = null, albumName = null;
        if (t.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object)
        {
            albumId = RequiredId(album, "id"); albumName = RequiredText(album, "name", 400);
        }
        DateTime? savedAt = null;
        if (Text(item, "added_at") is { } date)
        {
            if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) throw Invalid("A saved track had an invalid added_at date.");
            savedAt = parsed.UtcDateTime;
        }
        var duration = Integer(t, "duration_ms");
        if (duration is <= 0) duration = null;
        var isrc = t.TryGetProperty("external_ids", out var external) ? Text(external, "isrc") : null;
        if (isrc is { Length: > 16 }) throw Invalid("A saved track had an invalid ISRC.");
        return new(id!, name, savedAt, duration, isrc, albumId, albumName, artistRows);
    }

    private static bool ValidId(string? value) => value is { Length: 22 } && value.All(char.IsAsciiLetterOrDigit);
    private static string RequiredId(JsonElement e, string key) => Text(e, key) is { } id && ValidId(id) ? id : throw Invalid("Spotify returned invalid music metadata identity.");
    private static string RequiredText(JsonElement e, string key, int max) => Text(e, key) is { } s && !string.IsNullOrWhiteSpace(s) && s.Length <= max && NameNormalizer.Normalize(s).Length <= max ? s : throw Invalid("Spotify returned missing or oversized music metadata.");
    private static string? Text(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Integer(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
    private static SpotifyConnectionException Invalid(string message) => new(message);
}
