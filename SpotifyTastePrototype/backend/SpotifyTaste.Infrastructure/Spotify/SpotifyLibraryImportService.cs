using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;

namespace SpotifyTaste.Infrastructure.Spotify;

public sealed record SpotifyLibraryImportResult(long BatchId, int SpotifyTotal, int SavedTracks, int Added, int Updated, int Removed, int NewTracks, DateTime CompletedAtUtc);

public sealed class SpotifyLibraryArchive(string directory)
{
    // Private recovery copy of the previous local saved-state snapshot, not raw API data.
    public async Task WriteAsync(long batchId, IReadOnlyList<UserSavedTrack> previous, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(previous.Select(s => new { s.UserId, s.TrackId, s.SavedAtUtc, s.Source, s.ImportedAtUtc }));
        var path = Path.Combine(directory, $"before-library-import-{batchId}-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json, ct);
    }
}

public sealed class SpotifyLibraryImportService(SpotifyTasteDbContext db, ISpotifyLibraryClient client, SpotifyLibraryArchive archive)
{
    public const string Source = "SpotifyWebApiSavedTracks";
    public async Task<SpotifyLibraryImportResult> ImportAsync(CancellationToken ct)
    {
        var batch = new ImportBatch { Source = Source, FileName = "Spotify Web API /me/tracks (complete saved-library snapshot)", StartedAtUtc = DateTime.UtcNow, Status = ImportBatchStatus.Running };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        try
        {
            var snapshot = await client.ReadAllAsync(ct); // No saved-state mutation until every page is valid.
            batch.RecordsFound = snapshot.Total;
            if (snapshot.Songs.Count != snapshot.Total || snapshot.Songs.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Total)
                throw new SpotifyConnectionException("Library snapshot was incomplete or contained duplicate identities. Previous saved state is unchanged.");
            return await ApplySnapshotAsync(batch, snapshot, ct);
        }
        catch (Exception ex)
        {
            // Never store response bodies, bearer tokens, OAuth codes or arbitrary exception messages.
            db.ChangeTracker.Clear();
            var failed = await db.ImportBatches.SingleAsync(x => x.Id == batch.Id, CancellationToken.None);
            failed.Status = ImportBatchStatus.Failed; failed.CompletedAtUtc = DateTime.UtcNow; failed.RecordsFound = batch.RecordsFound;
            db.ImportErrors.Add(new ImportError { ImportBatchId = batch.Id, FileName = batch.FileName,
                ErrorType = ex is OperationCanceledException ? "Cancelled" : "LibrarySnapshotFailed",
                ErrorMessage = ex is SpotifyConnectionException ? ex.Message : "Library sync did not complete. Previous saved-state snapshot was preserved. Retry the import." });
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<SpotifyLibraryImportResult> ApplySnapshotAsync(ImportBatch batch, SpotifyLibrarySnapshot snapshot, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var now = DateTime.UtcNow;
        var user = await db.Users.SingleAsync(ct);
        var previous = await db.UserSavedTracks.Where(x => x.UserId == user.Id).ToListAsync(ct);
        await archive.WriteAsync(batch.Id, previous, ct); // Failure to make a recovery copy aborts the replacement.
        var savedByTrack = previous.ToDictionary(s => s.TrackId);
        var tracks = await db.Tracks.Where(t => t.SpotifyTrackId != null).ToDictionaryAsync(t => t.SpotifyTrackId!, StringComparer.Ordinal, ct);
        var artists = await db.Artists.Where(a => a.SpotifyArtistId != null).ToDictionaryAsync(a => a.SpotifyArtistId!, StringComparer.Ordinal, ct);
        var albums = await db.Albums.Where(a => a.SpotifyAlbumId != null).ToDictionaryAsync(a => a.SpotifyAlbumId!, StringComparer.Ordinal, ct);
        var keep = new HashSet<long>();
        var added = 0; var updated = 0; var newTracks = 0;
        foreach (var song in snapshot.Songs)
        {
            if (!tracks.TryGetValue(song.Id, out var track))
            {
                var collaborators = song.Artists.Select(a =>
                {
                    if (!artists.TryGetValue(a.Id, out var artist))
                    {
                        // An exact provider ID is evidence. A matching name alone is not.
                        artist = new Artist { SpotifyArtistId = a.Id, Name = a.Name, NormalizedName = NameNormalizer.Normalize(a.Name), CreatedAtUtc = now, UpdatedAtUtc = now };
                        db.Artists.Add(artist); artists.Add(a.Id, artist);
                    }
                    return artist;
                }).ToArray();
                Album? album = null;
                if (song.AlbumId is { } albumId && !albums.TryGetValue(albumId, out album))
                {
                    album = new Album { SpotifyAlbumId = albumId, Name = song.AlbumName!, NormalizedName = NameNormalizer.Normalize(song.AlbumName!), CreatedAtUtc = now, UpdatedAtUtc = now };
                    db.Albums.Add(album); albums.Add(albumId, album);
                }
                track = new Track { SpotifyTrackId = song.Id, SpotifyUri = $"spotify:track:{song.Id}", SpotifyUrl = $"https://open.spotify.com/track/{song.Id}",
                    Name = song.Name, NormalizedName = NameNormalizer.Normalize(song.Name), Album = album, DurationMs = song.DurationMs, Isrc = song.Isrc,
                    CreatedAtUtc = now, UpdatedAtUtc = now,
                    TrackArtists = collaborators.Select((a, index) => new TrackArtist { Artist = a, ArtistOrder = (short)index }).ToList() };
                db.Tracks.Add(track); tracks.Add(song.Id, track); newTracks++;
                db.TrackExternalIds.Add(new TrackExternalId { Track = track, Provider = "Spotify", ExternalId = song.Id, CreatedAtUtc = now });
            }
            // Existing history metadata and fact rows are left intact. Enrichment remains
            // the metadata authority for them; API import only changes real saved state.
            if (track.Id != 0 && savedByTrack.TryGetValue(track.Id, out var saved))
            {
                saved.SavedAtUtc = song.SavedAtUtc; saved.Source = Source; saved.ImportedAtUtc = now; updated++; keep.Add(track.Id);
            }
            else
            {
                db.UserSavedTracks.Add(new UserSavedTrack { UserId = user.Id, Track = track, SavedAtUtc = song.SavedAtUtc, Source = Source, ImportedAtUtc = now });
                added++;
            }
        }
        var removed = previous.Where(s => !keep.Contains(s.TrackId)).ToArray();
        db.UserSavedTracks.RemoveRange(removed); // Current snapshot semantics, with recovery copy above.
        user.SpotifyLibraryDataAvailable = true; user.UpdatedAtUtc = now;
        batch.RecordsImported = snapshot.Songs.Count; batch.Status = ImportBatchStatus.Completed; batch.CompletedAtUtc = now;
        await db.SaveChangesAsync(ct);
        if (await db.UserSavedTracks.CountAsync(s => s.UserId == user.Id, ct) != snapshot.Total)
            throw new SpotifyConnectionException("Saved-track count did not reconcile; the SQL transaction was rolled back.");
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(batch.Id, snapshot.Total, snapshot.Total, added, updated, removed.Length, newTracks, now);
    }
}
