using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SpotifyTaste.Application;

/// <summary>
/// Describes the evidence used to identify a recording. A Spotify track id is
/// release-specific; ISRC and conservative metadata keys may span releases.
/// </summary>
public enum RecordingIdentityBasis
{
    Isrc,
    ConservativeMetadata,
    SpotifyTrackId
}

public readonly record struct RecordingIdentityKey(string Value, RecordingIdentityBasis Basis)
{
    public bool CanGroupAcrossReleases => Basis is RecordingIdentityBasis.Isrc or RecordingIdentityBasis.ConservativeMetadata;

    public override string ToString() => Value;
}

public sealed record RecordingIdentityInput(
    string? Isrc,
    string? SpotifyTrackId,
    string? TrackName,
    IReadOnlyList<string>? Artists,
    string? AlbumName,
    int? DurationMs);

/// <summary>
/// Resolves a stable, deliberately conservative recording key.
///
/// A valid ISRC is the primary signal, but provider ISRC collisions are guarded
/// by a coarse duration bucket and an explicit-version discriminator. When ISRC
/// is unavailable, metadata grouping is allowed only for an exact normalized
/// title, ordered artist credit, and rounded duration, and only when no
/// alternate-version marker is present. Otherwise the Spotify track id is
/// retained as a release-specific identity.
/// </summary>
public static partial class RecordingIdentityResolver
{
    private const int MinimumPlausibleDurationMs = 1_000;
    private const int MaximumPlausibleDurationMs = 4 * 60 * 60 * 1_000;

    public static RecordingIdentityKey? Resolve(RecordingIdentityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var crossReleaseKey = TryCreateCrossReleaseKey(input);
        if (crossReleaseKey is not null) return crossReleaseKey;

        var spotifyTrackId = NormalizeSpotifyTrackId(input.SpotifyTrackId);
        return spotifyTrackId is null
            ? null
            : new RecordingIdentityKey($"spotify:{spotifyTrackId}", RecordingIdentityBasis.SpotifyTrackId);
    }

    /// <summary>
    /// Returns only an identity that is safe to use across Spotify releases.
    /// </summary>
    public static RecordingIdentityKey? TryCreateCrossReleaseKey(RecordingIdentityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var isrc = NormalizeIsrc(input.Isrc);
        if (isrc is not null)
        {
            return CreateGuardedIsrcKey(input, isrc);
        }

        return TryCreateMetadataKey(input);
    }

    /// <summary>
    /// Compares two provider entries as recordings. Exact Spotify identity wins.
    /// Same-ISRC entries still require compatible duration/version metadata to
    /// guard known provider collisions. Different or missing ISRCs may merge only
    /// on exact title + ordered artists with strong duration/release evidence.
    /// </summary>
    public static bool AreEquivalent(RecordingIdentityInput left, RecordingIdentityInput right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftSpotifyId = NormalizeSpotifyTrackId(left.SpotifyTrackId);
        var rightSpotifyId = NormalizeSpotifyTrackId(right.SpotifyTrackId);
        if (leftSpotifyId is not null && rightSpotifyId is not null &&
            string.Equals(leftSpotifyId, rightSpotifyId, StringComparison.Ordinal))
        {
            return true;
        }

        var leftIsrc = NormalizeIsrc(left.Isrc);
        var rightIsrc = NormalizeIsrc(right.Isrc);
        var titleEqual = SameNormalizedText(left.TrackName, right.TrackName);
        var artistsEqual = SameArtists(left.Artists, right.Artists);
        var durationCompatible = CompatibleDuration(left.DurationMs, right.DurationMs);
        var leftAlternate = HasExplicitAlternateVersion(left.TrackName, left.AlbumName);
        var rightAlternate = HasExplicitAlternateVersion(right.TrackName, right.AlbumName);

        if (leftIsrc is not null && rightIsrc is not null &&
            string.Equals(leftIsrc, rightIsrc, StringComparison.Ordinal))
        {
            if (!durationCompatible || leftAlternate != rightAlternate) return false;
            if (leftAlternate) return titleEqual && artistsEqual;
            return titleEqual || (artistsEqual && TitlesCompatible(left.TrackName, right.TrackName));
        }

        // Cross-release fallback handles cases such as an original album and a
        // later compilation receiving different ISRCs. Version labels are never
        // stripped, and both known durations must agree within two seconds.
        if (titleEqual && artistsEqual && durationCompatible && !leftAlternate && !rightAlternate)
            return true;

        // Lower-confidence release-scoped fallback for incomplete provider data:
        // exact title, artist credit, and album may connect a relinked Spotify ID
        // only when at least one side lacks ISRC or duration.
        var incomplete = leftIsrc is null || rightIsrc is null || !HasPlausibleDuration(left.DurationMs) || !HasPlausibleDuration(right.DurationMs);
        return incomplete && titleEqual && artistsEqual && SameNormalizedText(left.AlbumName, right.AlbumName);
    }

    /// <summary>
    /// Returns an aligned cluster id for each input. Candidate buckets keep this
    /// near-linear for a personal library while pairwise equivalence avoids the
    /// boundary errors caused by rounded-duration identity keys.
    /// </summary>
    public static IReadOnlyList<int> Cluster(IReadOnlyList<RecordingIdentityInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var parent = Enumerable.Range(0, inputs.Count).ToArray();
        var buckets = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var index = 0; index < inputs.Count; index++)
        {
            foreach (var bucket in CandidateBuckets(inputs[index]))
            {
                if (!buckets.TryGetValue(bucket, out var values)) buckets[bucket] = values = [];
                values.Add(index);
            }
        }

        foreach (var values in buckets.Values.Where(x => x.Count > 1))
        {
            for (var left = 0; left < values.Count - 1; left++)
            for (var right = left + 1; right < values.Count; right++)
            {
                if (AreEquivalent(inputs[values[left]], inputs[values[right]]))
                    Union(values[left], values[right]);
            }
        }

        var roots = new int[inputs.Count];
        for (var index = 0; index < roots.Length; index++) roots[index] = Find(index);
        return roots;

        int Find(int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }
            return value;
        }

        void Union(int left, int right)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);
            if (leftRoot != rightRoot) parent[Math.Max(leftRoot, rightRoot)] = Math.Min(leftRoot, rightRoot);
        }
    }

    /// <summary>
    /// Accepts compact or conventionally hyphenated ISRC values and returns the
    /// canonical uppercase 12-character alphanumeric code. Invalid values are
    /// treated as missing.
    /// </summary>
    public static string? NormalizeIsrc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var compact = IsrcSeparators().Replace(value.Trim(), string.Empty).ToUpperInvariant();
        return ValidIsrc().IsMatch(compact) ? compact : null;
    }

    public static bool HasExplicitAlternateVersion(string? trackName, string? albumName) =>
        ContainsTrackVersionMarker(trackName) || ContainsAlbumVersionMarker(albumName);

    private static RecordingIdentityKey CreateGuardedIsrcKey(RecordingIdentityInput input, string isrc)
    {
        var durationBucket = input.DurationMs is >= MinimumPlausibleDurationMs and <= MaximumPlausibleDurationMs
            ? $"d{((input.DurationMs.Value + 2_500) / 5_000) * 5}"
            : "d?";
        var version = "standard";
        if (HasExplicitAlternateVersion(input.TrackName, input.AlbumName))
        {
            // Alternate recordings sharing an ISRC are only grouped when their
            // displayed track labels agree. Release packaging is intentionally
            // omitted, so the same named remix/remaster can still span albums.
            var label = NameNormalizer.Normalize(input.TrackName ?? input.AlbumName ?? string.Empty);
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(label)))[..16];
            version = $"alternate:{fingerprint}";
        }

        return new RecordingIdentityKey($"isrc:{isrc}:{durationBucket}:{version}", RecordingIdentityBasis.Isrc);
    }

    private static IEnumerable<string> CandidateBuckets(RecordingIdentityInput input)
    {
        var spotifyId = NormalizeSpotifyTrackId(input.SpotifyTrackId);
        if (spotifyId is not null) yield return $"spotify:{spotifyId}";

        var isrc = NormalizeIsrc(input.Isrc);
        if (isrc is not null) yield return $"isrc:{isrc}";

        var title = NormalizeOptional(input.TrackName);
        var artists = NormalizeArtists(input.Artists);
        if (title is null || artists is null) yield break;
        yield return $"metadata:{title.Length}:{title}|{artists}";

        var album = NormalizeOptional(input.AlbumName);
        if (album is not null) yield return $"release:{title.Length}:{title}|{artists}|{album.Length}:{album}";
    }

    private static bool CompatibleDuration(int? left, int? right) =>
        HasPlausibleDuration(left) && HasPlausibleDuration(right) && Math.Abs(left!.Value - right!.Value) <= 2_000;

    private static bool HasPlausibleDuration(int? value) =>
        value is >= MinimumPlausibleDurationMs and <= MaximumPlausibleDurationMs;

    private static bool SameNormalizedText(string? left, string? right)
    {
        var normalizedLeft = NormalizeOptional(left);
        var normalizedRight = NormalizeOptional(right);
        return normalizedLeft is not null && normalizedRight is not null &&
            string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static bool SameArtists(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        var normalizedLeft = NormalizeArtists(left);
        var normalizedRight = NormalizeArtists(right);
        return normalizedLeft is not null && normalizedRight is not null &&
            string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : NameNormalizer.Normalize(value);

    private static string? NormalizeArtists(IReadOnlyList<string>? artists)
    {
        if (artists is null) return null;
        var normalized = artists.Where(x => !string.IsNullOrWhiteSpace(x)).Select(NameNormalizer.Normalize).ToArray();
        return normalized.Length == 0 ? null : string.Join('\u001F', normalized);
    }

    private static bool TitlesCompatible(string? left, string? right)
    {
        var leftTitle = NormalizeOptional(left);
        var rightTitle = NormalizeOptional(right);
        if (leftTitle is null || rightTitle is null) return false;
        if (string.Equals(leftTitle, rightTitle, StringComparison.Ordinal)) return true;

        var leftWords = TitleWord().Matches(leftTitle).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        var rightWords = TitleWord().Matches(rightTitle).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        if (leftWords.Count == 0 || rightWords.Count == 0) return false;
        var shared = leftWords.Count(rightWords.Contains);
        return shared >= 2 && shared / (double)Math.Min(leftWords.Count, rightWords.Count) >= 0.75;
    }

    private static RecordingIdentityKey? TryCreateMetadataKey(RecordingIdentityInput input)
    {
        if (string.IsNullOrWhiteSpace(input.TrackName) ||
            input.Artists is null ||
            input.DurationMs is not (>= MinimumPlausibleDurationMs and <= MaximumPlausibleDurationMs) ||
            HasExplicitAlternateVersion(input.TrackName, input.AlbumName))
        {
            return null;
        }

        var title = NameNormalizer.Normalize(input.TrackName);
        var artists = input.Artists
            .Where(artist => !string.IsNullOrWhiteSpace(artist))
            .Select(NameNormalizer.Normalize)
            .ToArray();

        if (title.Length == 0 || artists.Length == 0) return null;

        // Provider durations can vary by a few milliseconds. Whole-second
        // rounding absorbs that noise without broadly grouping similar edits.
        var durationSecond = (input.DurationMs.Value + 500) / 1_000;
        var canonical = BuildLengthPrefixedValue(title, artists, durationSecond);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new RecordingIdentityKey($"metadata:v1:{fingerprint}", RecordingIdentityBasis.ConservativeMetadata);
    }

    private static string BuildLengthPrefixedValue(string title, IReadOnlyList<string> artists, int durationSecond)
    {
        var builder = new StringBuilder();
        AppendSegment(builder, title);
        builder.Append(artists.Count).Append(':');
        foreach (var artist in artists) AppendSegment(builder, artist);
        builder.Append(durationSecond);
        return builder.ToString();
    }

    private static void AppendSegment(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value);

    private static string? NormalizeSpotifyTrackId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return ValidSpotifyTrackId().IsMatch(trimmed) ? trimmed : null;
    }

    private static bool ContainsTrackVersionMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Normalize(NormalizationForm.FormKC);
        return TrackVersionMarker().IsMatch(normalized) || VersionPrefix().IsMatch(normalized);
    }

    private static bool ContainsAlbumVersionMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Normalize(NormalizationForm.FormKC);
        return AlbumVersionMarker().IsMatch(normalized);
    }

    [GeneratedRegex(@"[\s-]+")]
    private static partial Regex IsrcSeparators();

    [GeneratedRegex(@"^[A-Z0-9]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidIsrc();

    [GeneratedRegex(@"^[A-Za-z0-9]{22}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidSpotifyTrackId();

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TitleWord();

    // A marker must occur in a suffix/qualifier. This avoids treating titles
    // such as "Live and Let Die" or "Mixed Emotions" as alternate versions.
    [GeneratedRegex(@"(?:\s[-–—/]\s*|[([]\s*)(?:[^\])]*\b)?(?:live(?:\s+(?:at|from|in|on|version|recording))?|remix(?:ed)?|remaster(?:ed)?|acoustic|unplugged|demo|radio\s+(?:edit|mix|version)|extended\s+(?:edit|mix|version)|single\s+version|album\s+version|alternate(?:\s+(?:take|version|mix))?|instrumental|karaoke|club\s+mix|dub\s+mix|pop\s+mix|dance\s+mix|mono(?:\s+(?:version|mix))?|stereo(?:\s+(?:version|mix))?|sped\s*up|slowed(?:\s+down)?|reverb(?:ed)?|re-record(?:ed|ing)?|edit|mix)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrackVersionMarker();

    [GeneratedRegex(@"^(?:live\s+(?:at|from|in|on)|acoustic\s+version|radio\s+edit|demo\s+version)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionPrefix();

    [GeneratedRegex(@"\b(?:live\s+(?:at|from|in|on)|live\s+(?:album|recording)|remix(?:es|ed)?|remaster(?:ed)?|acoustic\s+(?:album|version|sessions?)|unplugged|demo\s+(?:album|recordings?)|radio\s+(?:edit|mix|version)|instrumental|karaoke|club\s+mix|dub\s+mix|mono\s+(?:version|mix)|stereo\s+(?:version|mix)|re-record(?:ed|ing)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlbumVersionMarker();
}
