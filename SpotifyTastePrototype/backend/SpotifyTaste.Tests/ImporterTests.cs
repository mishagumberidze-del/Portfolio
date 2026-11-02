using Microsoft.EntityFrameworkCore;
using SpotifyTaste.Infrastructure.Import;
using SpotifyTaste.Infrastructure.Persistence;

namespace SpotifyTaste.Tests;

public sealed class ImporterTests
{
    [Fact]
    public async Task Importer_filters_podcasts_tracks_errors_and_is_idempotent()
    {
        using var temp = new TempDirectory();
        var json = """
        [
          {"ts":"2026-01-01T00:00:00Z","platform":"windows","ms_played":180000,"master_metadata_track_name":"Good Track","master_metadata_album_artist_name":"Good Artist","master_metadata_album_album_name":"Good Album","spotify_track_uri":"spotify:track:6fOqEAXCwboCO4BdzbZG1K","reason_start":"clickrow","reason_end":"trackdone","shuffle":false,"skipped":false,"offline":false,"incognito_mode":false},
          {"ts":"2026-01-01T00:00:00Z","platform":"windows","ms_played":180000,"master_metadata_track_name":"Good Track","master_metadata_album_artist_name":"Good Artist","master_metadata_album_album_name":"Good Album","spotify_track_uri":"spotify:track:6fOqEAXCwboCO4BdzbZG1K","reason_start":"clickrow","reason_end":"trackdone","shuffle":false,"skipped":false,"offline":false,"incognito_mode":false},
          {"ts":"2026-01-02T00:00:00Z","ms_played":90000,"episode_name":"Podcast","spotify_episode_uri":"spotify:episode:123"},
          {"ts":"2026-01-03T00:00:00Z","ms_played":1000,"master_metadata_track_name":"Broken","spotify_track_uri":"spotify:track:0123456789012345678901"}
        ]
        """;
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "Streaming_History_Audio_2026.json"), json);
        await using var db = CreateContext();
        var importer = new SpotifyImportService(db, new SpotifyExportReader());

        var first = await importer.ImportAsync(temp.Path);
        var second = await importer.ImportAsync(temp.Path);

        Assert.Equal(4, first.RecordsFound);
        Assert.Equal(1, first.RecordsImported);
        Assert.Equal(1, first.Duplicates);
        Assert.Equal(1, first.IgnoredNonMusicRecords);
        Assert.Equal(1, first.InvalidRecords);
        Assert.Equal(0, second.RecordsImported);
        Assert.Equal(2, second.Duplicates);
        Assert.Single(await db.ListeningEvents.ToListAsync());
        Assert.Single(await db.ImportErrors.Where(x => x.ImportBatchId == first.ImportBatchId).ToListAsync());
    }

    [Fact]
    public void Event_hash_changes_when_a_legitimate_event_field_changes()
    {
        var when = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var record = new SpotifyHistoryRecord { SpotifyTrackUri = "spotify:track:6fOqEAXCwboCO4BdzbZG1K", MsPlayed = 10_000, Platform = "windows" };
        var original = SpotifyImportService.CreateEventHash(1, record, when);
        record.MsPlayed++;
        Assert.NotEqual(original, SpotifyImportService.CreateEventHash(1, record, when));
        Assert.Equal(64, original.Length);
    }

    [Fact]
    public async Task Library_status_is_confirmed_only_by_a_valid_tracks_array_and_is_not_reset()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "Streaming_History_Audio_2026.json"),
            "[{\"ts\":\"2026-01-01T00:00:00Z\",\"ms_played\":120000,\"master_metadata_track_name\":\"Saved Track\",\"master_metadata_album_artist_name\":\"Artist\",\"master_metadata_album_album_name\":\"Album\",\"spotify_track_uri\":\"spotify:track:6fOqEAXCwboCO4BdzbZG1K\"}]");
        var libraryPath = Path.Combine(temp.Path, "YourLibrary.json");
        await File.WriteAllTextAsync(libraryPath,
            "{\"tracks\":[{\"track\":\"Saved Track\",\"artist\":\"Artist\",\"album\":\"Album\",\"uri\":\"spotify:track:6fOqEAXCwboCO4BdzbZG1K\"}]}");
        await using var db = CreateContext();
        var importer = new SpotifyImportService(db, new SpotifyExportReader());

        var withLibrary = await importer.ImportAsync(temp.Path);
        File.Delete(libraryPath);
        var historyOnly = await importer.ImportAsync(temp.Path);

        Assert.True(withLibrary.LibraryDataAvailable);
        Assert.Equal(1, withLibrary.SavedTrackCount);
        Assert.True(historyOnly.LibraryDataAvailable);
        Assert.Equal(1, historyOnly.SavedTrackCount);
    }

    [Fact]
    public async Task File_named_library_without_tracks_array_does_not_claim_saved_state()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "Streaming_History_Audio_2026.json"),
            "[{\"ts\":\"2026-01-01T00:00:00Z\",\"ms_played\":120000,\"master_metadata_track_name\":\"Track\",\"master_metadata_album_artist_name\":\"Artist\",\"master_metadata_album_album_name\":\"Album\",\"spotify_track_uri\":\"spotify:track:6fOqEAXCwboCO4BdzbZG1K\"}]");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "SomeLibraryNotes.json"), "{\"notTracks\":[]}");
        await using var db = CreateContext();

        var report = await new SpotifyImportService(db, new SpotifyExportReader()).ImportAsync(temp.Path);

        Assert.False(report.LibraryDataAvailable);
        Assert.Equal(0, report.SavedTrackCount);
    }

    private static SpotifyTasteDbContext CreateContext() => new(new DbContextOptionsBuilder<SpotifyTasteDbContext>()
        .UseInMemoryDatabase($"spotify-import-{Guid.NewGuid():N}").Options);
}
