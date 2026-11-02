using SpotifyTaste.Application;

namespace SpotifyTaste.Tests;

public sealed class RecordingIdentityTests
{
    private const string FirstSpotifyId = "60a0Rd6pjrkxjPbaKzXjfq";
    private const string SecondSpotifyId = "7q115ia4fQn9zonjpexWsY";

    [Theory]
    [InlineData("USWB10002407")]
    [InlineData("uswb10002407")]
    [InlineData("US-WB1-00-02407")]
    [InlineData(" US WB1 00 02407 ")]
    public void Isrc_is_normalized_to_a_compact_uppercase_code(string value) =>
        Assert.Equal("USWB10002407", RecordingIdentityResolver.NormalizeIsrc(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("USWB1000240")]
    [InlineData("USWB10002407X")]
    [InlineData("US.WB1.00.02407")]
    [InlineData("USWB10002!07")]
    public void Invalid_isrc_is_treated_as_missing(string? value) =>
        Assert.Null(RecordingIdentityResolver.NormalizeIsrc(value));

    [Fact]
    public void Same_isrc_groups_standard_deluxe_and_compilation_releases()
    {
        var standard = Input("US-WB1-00-02407", FirstSpotifyId, "In the End", "Linkin Park", "Hybrid Theory", 216_880);
        var deluxe = Input("uswb10002407", SecondSpotifyId, "In the End", "Linkin Park", "Hybrid Theory (Deluxe Edition)", 216_880);
        var compilation = Input("USWB10002407", "1A2b3C4d5E6f7G8h9I0jK1", "In the End", "Linkin Park", "Greatest Hits", 216_900);

        var key = RecordingIdentityResolver.Resolve(standard);

        Assert.Equal(RecordingIdentityBasis.Isrc, key?.Basis);
        Assert.True(key?.CanGroupAcrossReleases);
        Assert.Equal(key, RecordingIdentityResolver.Resolve(deluxe));
        Assert.Equal(key, RecordingIdentityResolver.Resolve(compilation));
        Assert.True(RecordingIdentityResolver.AreEquivalent(standard, deluxe));
    }

    [Fact]
    public void Differing_valid_isrcs_can_join_through_exact_metadata_and_duration()
    {
        var first = Input("USWB10002407", FirstSpotifyId, "In the End", "Linkin Park", "Hybrid Theory", 216_880);
        var second = Input("USWB10002408", SecondSpotifyId, "In the End", "Linkin Park", "Hybrid Theory (Deluxe Edition)", 216_880);

        Assert.NotEqual(RecordingIdentityResolver.Resolve(first), RecordingIdentityResolver.Resolve(second));
        Assert.True(RecordingIdentityResolver.AreEquivalent(first, second));
    }

    [Fact]
    public void Differing_valid_isrcs_with_different_titles_remain_separate()
    {
        var first = Input("USWB10002407", FirstSpotifyId, "In the End", "Linkin Park", "Hybrid Theory", 216_880);
        var second = Input("USWB10002408", SecondSpotifyId, "Numb", "Linkin Park", "Meteora", 216_880);

        Assert.False(RecordingIdentityResolver.AreEquivalent(first, second));
    }

    [Fact]
    public void Same_isrc_does_not_collapse_standard_and_explicit_remaster()
    {
        var original = Input("GBUM71029606", FirstSpotifyId, "Killer Queen", "Queen", "Sheer Heart Attack", 179_355);
        var remaster = Input("GBUM71029606", SecondSpotifyId, "Killer Queen - Remastered 2011", "Queen", "Sheer Heart Attack (Remastered)", 179_600);

        Assert.NotEqual(RecordingIdentityResolver.Resolve(original), RecordingIdentityResolver.Resolve(remaster));
        Assert.False(RecordingIdentityResolver.AreEquivalent(original, remaster));
    }

    [Fact]
    public void Same_isrc_with_incompatible_durations_does_not_merge_provider_collision()
    {
        var first = Input("RUA1D2267741", FirstSpotifyId, "Белая ночь", "Artist", "Album", 238_548);
        var unrelated = Input("RUA1D2267741", SecondSpotifyId, "Ночное рандеву", "Artist", "Album", 208_027);

        Assert.NotEqual(RecordingIdentityResolver.Resolve(first), RecordingIdentityResolver.Resolve(unrelated));
        Assert.False(RecordingIdentityResolver.AreEquivalent(first, unrelated));
    }

    [Fact]
    public void Safe_metadata_fallback_ignores_standard_deluxe_and_compilation_album_packaging()
    {
        var standard = Input(null, FirstSpotifyId, "I Don't Want to Miss a Thing", "Aerosmith", "Armageddon", 298_760);
        var deluxe = Input(null, SecondSpotifyId, "  I Don't Want to Miss a Thing ", "aerosmith", "Armageddon (Deluxe Edition)", 298_799);
        var compilation = Input(null, "1A2b3C4d5E6f7G8h9I0jK1", "I Don't Want to Miss a Thing", "Aerosmith", "Greatest Hits", 298_600);

        var key = RecordingIdentityResolver.Resolve(standard);

        Assert.Equal(RecordingIdentityBasis.ConservativeMetadata, key?.Basis);
        Assert.True(key?.CanGroupAcrossReleases);
        Assert.Equal(key, RecordingIdentityResolver.Resolve(deluxe));
        Assert.Equal(key, RecordingIdentityResolver.Resolve(compilation));
    }

    [Theory]
    [InlineData("I Don't Want to Miss a Thing - Pop Mix", "Armageddon")]
    [InlineData("I Don't Want to Miss a Thing (Live)", "Armageddon")]
    [InlineData("I Don't Want to Miss a Thing - 2024 Remaster", "Armageddon")]
    [InlineData("I Don't Want to Miss a Thing - Acoustic", "Armageddon")]
    [InlineData("I Don't Want to Miss a Thing - Radio Edit", "Armageddon")]
    [InlineData("I Don't Want to Miss a Thing", "Live at Wembley")]
    [InlineData("I Don't Want to Miss a Thing", "Armageddon Remastered")]
    public void Explicit_alternate_versions_do_not_use_metadata_fallback(string track, string album)
    {
        var input = Input(null, SecondSpotifyId, track, "Aerosmith", album, 298_760);

        var key = RecordingIdentityResolver.Resolve(input);

        Assert.Equal(RecordingIdentityBasis.SpotifyTrackId, key?.Basis);
        Assert.False(key?.CanGroupAcrossReleases);
        Assert.Null(RecordingIdentityResolver.TryCreateCrossReleaseKey(input));
    }

    [Fact]
    public void Different_alternate_releases_remain_distinct_by_spotify_track_id()
    {
        var live = Input(null, FirstSpotifyId, "Song (Live)", "Artist", "Concert", 200_000);
        var acoustic = Input(null, SecondSpotifyId, "Song - Acoustic", "Artist", "Album", 200_000);

        Assert.NotEqual(RecordingIdentityResolver.Resolve(live), RecordingIdentityResolver.Resolve(acoustic));
        Assert.False(RecordingIdentityResolver.AreEquivalent(live, acoustic));
    }

    [Fact]
    public void Metadata_fallback_requires_same_ordered_artists_and_duration_second()
    {
        var original = new RecordingIdentityInput(null, FirstSpotifyId, "Song", ["Artist", "Guest"], "Album", 200_100);
        var reversedArtists = new RecordingIdentityInput(null, SecondSpotifyId, "Song", ["Guest", "Artist"], "Album Deluxe", 200_100);
        var differentDuration = new RecordingIdentityInput(null, SecondSpotifyId, "Song", ["Artist", "Guest"], "Album Deluxe", 202_200);

        Assert.False(RecordingIdentityResolver.AreEquivalent(original, reversedArtists));
        Assert.False(RecordingIdentityResolver.AreEquivalent(original, differentDuration));
    }

    [Fact]
    public void One_sided_isrc_can_fall_back_to_exact_metadata_and_duration()
    {
        var identified = Input("USWB10002407", FirstSpotifyId, "Song", "Artist", "Album", 200_000);
        var unidentified = Input(null, SecondSpotifyId, "Song", "Artist", "Album Deluxe", 200_000);

        Assert.True(RecordingIdentityResolver.AreEquivalent(identified, unidentified));
    }

    [Fact]
    public void Cluster_uses_pairwise_duration_tolerance_across_rounding_boundary()
    {
        var first = Input("USWB10002407", FirstSpotifyId, "Song", "Artist", "Album", 201_999);
        var second = Input("USWB10002408", SecondSpotifyId, "Song", "Artist", "Album Deluxe", 202_001);

        var clusters = RecordingIdentityResolver.Cluster([first, second]);

        Assert.Equal(clusters[0], clusters[1]);
    }

    [Fact]
    public void Release_scoped_fallback_links_exact_duplicate_with_missing_isrc_and_duration()
    {
        var identified = Input("USSM19801545", FirstSpotifyId, "I Don't Want to Miss a Thing - From Armageddon", "Aerosmith", "Armageddon", 299_723);
        var incomplete = Input(null, SecondSpotifyId, "I Don't Want to Miss a Thing - From Armageddon", "Aerosmith", "Armageddon", null);

        Assert.True(RecordingIdentityResolver.AreEquivalent(identified, incomplete));
        var clusters = RecordingIdentityResolver.Cluster([identified, incomplete]);
        Assert.Equal(clusters[0], clusters[1]);
    }

    [Fact]
    public void Exact_spotify_track_id_still_matches_when_isrc_is_missing_on_one_copy()
    {
        var identified = Input("USWB10002407", FirstSpotifyId, "Song", "Artist", "Album", 200_000);
        var unidentified = Input(null, FirstSpotifyId, null, "Artist", null, null);

        Assert.True(RecordingIdentityResolver.AreEquivalent(identified, unidentified));
    }

    [Theory]
    [InlineData("Live and Let Die")]
    [InlineData("Mixed Emotions")]
    [InlineData("Acoustic Hearts")]
    public void Ordinary_words_in_base_titles_are_not_mistaken_for_version_qualifiers(string title)
    {
        var input = Input(null, FirstSpotifyId, title, "Artist", "Album", 200_000);

        Assert.Equal(RecordingIdentityBasis.ConservativeMetadata, RecordingIdentityResolver.Resolve(input)?.Basis);
    }

    [Fact]
    public void Unresolvable_metadata_without_spotify_id_returns_null()
    {
        var input = new RecordingIdentityInput(null, null, "Song (Live)", ["Artist"], "Concert", null);

        Assert.Null(RecordingIdentityResolver.Resolve(input));
    }

    private static RecordingIdentityInput Input(
        string? isrc,
        string? spotifyTrackId,
        string? track,
        string artist,
        string? album,
        int? durationMs) =>
        new(isrc, spotifyTrackId, track, [artist], album, durationMs);
}
