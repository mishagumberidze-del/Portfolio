using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Analytics;

namespace SpotifyTaste.Tests;

public sealed class StoryTests
{
    [Fact]
    public void Returning_favorite_requires_recurrence_in_multiple_calendar_years()
    {
        var rows = Enumerable.Range(1, 3).Select(i => new StoryEvent(1, At(2025, 1, i), 190000, false)).ToArray();
        Assert.DoesNotContain(new StoryBuilder(rows, [Track(1)], [], new()).Build(2025).Facts, f => f.Title == "A returning favorite");
        var spanning = rows.Append(new StoryEvent(1, At(2024), 190000, false)).ToArray();
        Assert.Contains(new StoryBuilder(spanning, [Track(1)], [], new()).Build(2025).Facts, f => f.Title == "A returning favorite");
    }

    [Fact]
    public void Story_links_use_the_actual_representative_release_and_correct_resource_kind()
    {
        var first = Track(1); var deluxe = Track(2, album: "Album Deluxe");
        deluxe.Album!.SpotifyAlbumId = new string('a', 22);
        deluxe.TrackArtists.First().Artist.SpotifyArtistId = new string('b', 22);
        var result = new StoryBuilder([new(1, At(2025), 30000, false), new(2, At(2025), 200000, false)], [first, deluxe], [], new()).Build(2025);
        Assert.Equal("https://open.spotify.com/track/" + deluxe.SpotifyTrackId, result.TopSongs[0].SpotifyUrl);
        Assert.Equal("https://open.spotify.com/album/" + new string('a', 22), result.TopAlbums[0].SpotifyUrl);
        Assert.Equal("https://open.spotify.com/artist/" + new string('b', 22), result.TopArtists[0].SpotifyUrl);
        Assert.Equal(230000, result.Summary.TotalMs);
    }
    [Theory]
    [InlineData(null)] [InlineData("bad-id")] [InlineData("https://evil.invalid/")]
    public void Missing_or_malformed_provider_ids_do_not_create_story_links(string? id)
    {
        var track = Track(1); track.SpotifyTrackId = id;
        var result = new StoryBuilder([new(1, At(2025), 30000, false)], [track], [], new()).Build(2025);
        Assert.Null(result.TopSongs[0].SpotifyUrl); Assert.Null(result.TopArtists[0].SpotifyUrl); Assert.Null(result.TopAlbums[0].SpotifyUrl);
    }
    private static Track Track(long id, string name = "Song", string album = "Album", int duration = 200000) => new()
    {
        Id = id, Name = name, SpotifyTrackId = id.ToString().PadLeft(22, '0'), DurationMs = duration,
        AlbumId = id, Album = new Album { Id = id, Name = album },
        TrackArtists = [new TrackArtist { ArtistId = 1, Artist = new Artist { Id = 1, Name = "Artist" } }]
    };
    private static DateTime At(int y, int m = 1, int d = 1) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);
    [Fact]
    public void Year_boundaries_first_discoveries_and_release_rankings_are_recording_aware()
    {
        var builder = new StoryBuilder([new(1, At(2024, 12, 31).AddHours(23).AddMinutes(59), 100000, false), new(2, At(2025), 200000, false), new(2, At(2026), 30000, false)],
            [Track(1), Track(2, album: "Album Deluxe")], [], new());
        var year = builder.Build(2025);
        Assert.Equal(1, year.Summary.Events); Assert.Equal(200000, year.Summary.TotalMs);
        Assert.Equal(0, year.NewRecordings); Assert.Equal(0, year.NewArtists);
        Assert.Equal(At(2024, 12, 31).AddHours(23).AddMinutes(59), year.TopSongs[0].FirstListenUtc);
        Assert.Equal(1, year.TopSongs[0].PreviousRank); Assert.Equal(100d, year.Comparison!.ChangePercent);
        Assert.Equal(12, year.Journey.Count); Assert.Equal(0, year.Journey[1].TotalMs);
        Assert.Equal(1, builder.Build(null).Summary.Recordings); Assert.Equal(3, builder.Build(null).TopSongs[0].Events);
    }
    [Fact]
    public void Live_recordings_are_separate_and_raw_time_is_never_lost()
    {
        var result = new StoryBuilder([new(1, At(2025), 200000, false), new(2, At(2025), 200000, false), new(3, At(2025), 250000, false)],
            [Track(1), Track(2, album: "Deluxe"), Track(3, "Song - Live")], [], new()).Build(null);
        Assert.Equal(650000, result.Summary.TotalMs); Assert.Equal(2, result.Summary.Recordings);
        Assert.Equal(400000, result.TopSongs[0].TotalMs); Assert.Equal(1, result.Summary.ActiveDays);
        Assert.Equal(2, result.NewRecordings);
    }
    [Fact]
    public void Sound_profile_is_time_weighted_with_per_axis_validity_and_coverage()
    {
        var result = new StoryBuilder([new(1, At(2025), 300000, false), new(2, At(2025), 100000, false), new(3, At(2025), 100000, false)],
            [Track(1), Track(2, "Second"), Track(3, "Missing")],
            [new TrackFeature { TrackId = 1, Energy = 1, Tempo = 100 }, new TrackFeature { TrackId = 2, Energy = 0, Tempo = -5 }], new()).Build(2025);
        var energy = result.SoundProfile.Single(a => a.Name == "Energy");
        Assert.Equal(75d, energy.Value); Assert.Equal(80d, energy.CoveragePercent);
        Assert.Equal(100d, result.SoundProfile.Single(a => a.Name == "Tempo").Value);
        Assert.Equal(60d, result.SoundProfile.Single(a => a.Name == "Tempo").CoveragePercent);
        Assert.Null(result.SoundProfile.Single(a => a.Name == "Valence").Value);
    }
    [Fact]
    public void Empty_year_and_empty_history_have_no_invented_insights_or_division_by_zero()
    {
        var result = new StoryBuilder([], [], [], new()).Build(2025);
        Assert.Empty(result.TopSongs); Assert.Empty(result.Facts); Assert.Null(result.Comparison);
        Assert.Null(result.Summary.FirstListenUtc); Assert.Equal(0, result.Summary.TotalMs);
        Assert.All(result.SoundProfile, a => Assert.Null(a.Value)); Assert.Empty(result.FeatureInsights);
    }
    [Fact]
    public void First_year_does_not_invent_prior_rank_or_comparison()
    {
        var r = new StoryBuilder([new(1, At(2025), 30000, false)], [Track(1)], [], new()).Build(2025);
        Assert.Null(r.Comparison); Assert.Null(r.TopSongs[0].PreviousRank); Assert.Equal(1, r.NewRecordings);
        Assert.Equal(1, r.Summary.QualifiedPlays);
    }
    [Fact]
    public void Repeated_short_skips_do_not_create_a_returning_favorite_claim()
    {
        var rows = Enumerable.Range(1, 4).Select(i => new StoryEvent(1, At(2025, 1, i), 1000, true)).ToArray();
        var result = new StoryBuilder(rows, [Track(1)], [], new()).Build(2025);
        Assert.DoesNotContain(result.Facts, f => f.Title == "A returning favorite");
        Assert.Equal(0, result.Summary.QualifiedPlays);
        Assert.Equal(100m, result.Behavior.SkipRate);
    }
    [Fact]
    public void Feature_insights_require_a_real_shift_and_coverage_in_both_years()
    {
        var b = new StoryBuilder([new(1, At(2024), 30000, false), new(2, At(2025), 30000, false)],
            [Track(1), Track(2, "Second")], [new() { TrackId = 1, Energy = .3m }, new() { TrackId = 2, Energy = .6m }], new());
        Assert.Contains(b.Build(2025).FeatureInsights, i => i.Contains("higher") && i.Contains("energy"));
        Assert.Empty(b.Build(2024).FeatureInsights);
    }
    [Theory]
    [InlineData(0)] [InlineData(10000)]
    public void Invalid_years_are_rejected(int year) => Assert.Throws<ArgumentOutOfRangeException>(() => new StoryBuilder([], [], [], new()).Build(year));
}
