using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;
using SpotifyTaste.Infrastructure.Preferences;

namespace SpotifyTaste.Tests;

public sealed class SavedPreferenceIntegrationTests
{
    private static SpotifyTasteDbContext Db() => new(new DbContextOptionsBuilder<SpotifyTasteDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Track Track(char id, Artist artist, string title = "Same recording", string album = "Original") => new()
    {
        SpotifyTrackId = new string(id, 22), Name = title, NormalizedName = title.ToUpperInvariant(), DurationMs = 200000,
        Album = new Album { Name = album }, TrackArtists = [new TrackArtist { Artist = artist }]
    };

    [Fact]
    public async Task Saved_deluxe_release_boosts_original_but_not_live_and_creates_no_fake_plays()
    {
        await using var db = Db();
        var user = new User { SpotifyLibraryDataAvailable = true }; var artist = new Artist { Name = "One Artist" };
        var original = Track('A', artist); var deluxe = Track('B', artist, album: "Original (Deluxe)"); var live = Track('C', artist, title: "Same recording - Live");
        foreach (var track in new[] { original, live }) db.ListeningEvents.Add(new ListeningEvent { User = user, Track = track, PlayedAtUtc = DateTime.UtcNow, MsPlayed = 190000, WasSkipped = false });
        db.UserSavedTracks.Add(new UserSavedTrack { User = user, Track = deluxe });
        db.TrackPreferences.Add(new TrackPreference { User = user, Track = original, ModelVersion = "preference-v1", BehavioralScore = 3 });
        await db.SaveChangesAsync();
        var version = "preference-v1.1-saved-recordings";
        var model = new PreferenceModelService(db, Options.Create(new PreferenceModelOptions { ModelVersion = version }));
        var report = await model.RebuildAsync();
        Assert.Equal(2, report.TrackCount);
        Assert.Equal(2.5m, (await db.TrackPreferences.SingleAsync(p => p.TrackId == original.Id && p.ModelVersion == version)).SavedBoost);
        Assert.Equal(0m, (await db.TrackPreferences.SingleAsync(p => p.TrackId == live.Id && p.ModelVersion == version)).SavedBoost);
        Assert.Equal(3m, (await db.TrackPreferences.SingleAsync(p => p.ModelVersion == "preference-v1")).BehavioralScore);
        Assert.False(await db.TrackPreferences.AnyAsync(p => p.TrackId == deluxe.Id)); Assert.Equal(2, await db.ListeningEvents.CountAsync()); Assert.Single(db.UserSavedTracks);
        Assert.Contains("recording-out", report.ValidationMethod);
        db.UserSavedTracks.Remove(await db.UserSavedTracks.SingleAsync()); await db.SaveChangesAsync(); await model.RebuildAsync();
        Assert.Equal(0m, (await db.TrackPreferences.SingleAsync(p => p.TrackId == original.Id && p.ModelVersion == version)).SavedBoost);
    }

    [Fact]
    public async Task Unknown_library_does_not_enable_saved_boost_from_stray_rows()
    {
        await using var db = Db(); var user = new User { SpotifyLibraryDataAvailable = false }; var track = Track('A', new Artist { Name = "Artist" });
        db.ListeningEvents.Add(new ListeningEvent { User = user, Track = track, MsPlayed = 100000, PlayedAtUtc = DateTime.UtcNow });
        db.UserSavedTracks.Add(new UserSavedTrack { User = user, Track = track }); await db.SaveChangesAsync();
        await new PreferenceModelService(db).RebuildAsync(); Assert.Equal(0m, (await db.TrackPreferences.SingleAsync()).SavedBoost);
    }
}
