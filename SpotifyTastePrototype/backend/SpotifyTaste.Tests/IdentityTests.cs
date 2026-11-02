using SpotifyTaste.Application;

namespace SpotifyTaste.Tests;

public sealed class IdentityTests
{
    [Theory]
    [InlineData("spotify:track:6fOqEAXCwboCO4BdzbZG1K")]
    [InlineData("https://open.spotify.com/track/6fOqEAXCwboCO4BdzbZG1K?si=test")]
    [InlineData("https://open.spotify.com/intl-de/track/6fOqEAXCwboCO4BdzbZG1K")]
    [InlineData("6fOqEAXCwboCO4BdzbZG1K")]
    public void Spotify_track_identity_is_parsed_from_supported_forms(string value) =>
        Assert.Equal("6fOqEAXCwboCO4BdzbZG1K", SpotifyTrackIdParser.Parse(value));

    [Fact]
    public void Invalid_or_non_track_spotify_values_are_rejected() =>
        Assert.Throws<ArgumentException>(() => SpotifyTrackIdParser.Parse("https://open.spotify.com/album/6fOqEAXCwboCO4BdzbZG1K"));

    [Fact]
    public void Names_are_normalized_conservatively_without_removing_punctuation()
    {
        Assert.Equal("AC/DC & FRIENDS", NameNormalizer.Normalize("  AC/DC   &  Friends "));
        Assert.NotEqual(NameNormalizer.Normalize("Artist A"), NameNormalizer.Normalize("Artist-A"));
    }
}
