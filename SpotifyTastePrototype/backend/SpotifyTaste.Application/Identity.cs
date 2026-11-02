using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SpotifyTaste.Application;

public static partial class NameNormalizer
{
    public static string Normalize(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormKC);
        return Whitespace().Replace(normalized, " ").ToUpper(CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

public static partial class SpotifyTrackIdParser
{
    public static string Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A Spotify track URL or URI is required.", nameof(value));
        value = value.Trim();

        var uriMatch = SpotifyUri().Match(value);
        if (uriMatch.Success) return uriMatch.Groups[1].Value;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Host.Equals("open.spotify.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var trackIndex = Array.FindIndex(segments, x => x.Equals("track", StringComparison.OrdinalIgnoreCase));
            if (trackIndex >= 0 && trackIndex + 1 < segments.Length && SpotifyId().IsMatch(segments[trackIndex + 1])) return segments[trackIndex + 1];
        }

        if (SpotifyId().IsMatch(value)) return value;
        throw new ArgumentException("The value is not a valid Spotify track URL, URI, or 22-character track ID.", nameof(value));
    }

    [GeneratedRegex(@"^spotify:track:([A-Za-z0-9]{22})$", RegexOptions.IgnoreCase)]
    private static partial Regex SpotifyUri();

    [GeneratedRegex(@"^[A-Za-z0-9]{22}$")]
    private static partial Regex SpotifyId();
}

