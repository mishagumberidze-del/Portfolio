using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;

namespace SpotifyTaste.Infrastructure.Import;

public sealed class SpotifyImportService(SpotifyTasteDbContext dbContext, SpotifyExportReader exportReader) : ISpotifyImportService
{
    private const string HistorySource = "SpotifyExtendedHistory";
    private const string LibrarySource = "SpotifyYourLibrary";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<ImportReport> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var entries = await exportReader.DiscoverAsync(sourcePath, cancellationToken);
        var historyEntries = entries.Where(x => x.Kind == SpotifyExportEntryKind.ExtendedAudioHistory).ToArray();
        if (historyEntries.Length == 0) throw new InvalidDataException("No Extended Streaming History audio JSON files were found.");

        var now = DateTime.UtcNow;
        var batch = new ImportBatch
        {
            Source = HistorySource,
            FileName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            StartedAtUtc = now,
            Status = ImportBatchStatus.Running,
        };
        dbContext.ImportBatches.Add(batch);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var user = await dbContext.Users.SingleOrDefaultAsync(cancellationToken);
            if (user is null)
            {
                user = new User { DisplayName = "Local User", CreatedAtUtc = now, UpdatedAtUtc = now };
                dbContext.Users.Add(user);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var tracks = await dbContext.Tracks.Include(x => x.TrackArtists).ToDictionaryAsync(x => x.SpotifyUri!, StringComparer.OrdinalIgnoreCase, cancellationToken);
            var artists = (await dbContext.Artists.ToListAsync(cancellationToken)).GroupBy(x => x.NormalizedName, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var albumRows = await dbContext.Albums.Include(x => x.PrimaryArtist).ToListAsync(cancellationToken);
            var albums = albumRows.ToDictionary(x => AlbumKey(x.NormalizedName, x.PrimaryArtist?.NormalizedName ?? string.Empty), StringComparer.Ordinal);
            var eventHashes = (await dbContext.ListeningEvents.Select(x => x.ExternalEventHash).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
            var savedTrackIds = (await dbContext.UserSavedTracks.Where(x => x.UserId == user.Id).Select(x => x.TrackId).ToListAsync(cancellationToken)).ToHashSet();

            foreach (var entry in historyEntries)
                await ImportHistoryFileAsync(entry, batch, user, tracks, artists, albums, eventHashes, cancellationToken);

            var libraryEntries = entries.Where(x => x.Kind == SpotifyExportEntryKind.Library).ToArray();
            var validLibraryData = false;
            foreach (var entry in libraryEntries)
                validLibraryData |= await ImportLibraryFileAsync(entry, batch, user, tracks, artists, albums, savedTrackIds, cancellationToken);

            user.SpotifyLibraryDataAvailable |= validLibraryData;
            user.HistoryImportedAtUtc = DateTime.UtcNow;
            user.UpdatedAtUtc = DateTime.UtcNow;
            batch.CompletedAtUtc = DateTime.UtcNow;
            batch.Status = batch.InvalidRecords > 0 ? ImportBatchStatus.CompletedWithErrors : ImportBatchStatus.Completed;
            await dbContext.SaveChangesAsync(cancellationToken);

            return await CreateReportAsync(batch, user, cancellationToken);
        }
        catch (Exception exception)
        {
            batch.Status = ImportBatchStatus.Failed;
            batch.CompletedAtUtc = DateTime.UtcNow;
            batch.Errors.Add(new ImportError { FileName = batch.FileName, ErrorType = exception.GetType().Name, ErrorMessage = Truncate(exception.Message, 2000)! });
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task ImportHistoryFileAsync(
        SpotifyExportEntry entry,
        ImportBatch batch,
        User user,
        Dictionary<string, Track> tracks,
        Dictionary<string, Artist> artists,
        Dictionary<string, Album> albums,
        HashSet<string> eventHashes,
        CancellationToken cancellationToken)
    {
        await using var stream = await entry.OpenStreamAsync(cancellationToken);
        var records = await JsonSerializer.DeserializeAsync<List<SpotifyHistoryRecord>>(stream, JsonOptions, cancellationToken) ?? throw new InvalidDataException($"{entry.FileName} did not contain a JSON array.");
        var recordNumber = 0;
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            recordNumber++;
            batch.RecordsFound++;
            if (string.IsNullOrWhiteSpace(record.SpotifyTrackUri))
            {
                if (!string.IsNullOrWhiteSpace(record.SpotifyEpisodeUri) || !string.IsNullOrWhiteSpace(record.EpisodeName)) batch.IgnoredNonMusicRecords++;
                else AddError(batch, entry.FileName, recordNumber, "NonMusicOrMissingIdentity", "Record has no Spotify track or episode URI.");
                continue;
            }

            if (!TryValidate(record, out var spotifyTrackId, out var playedAtUtc, out var validationError))
            {
                AddError(batch, entry.FileName, recordNumber, "InvalidHistoryRecord", validationError!);
                continue;
            }

            var externalHash = CreateEventHash(user.Id, record, playedAtUtc);
            if (!eventHashes.Add(externalHash))
            {
                batch.Duplicates++;
                continue;
            }

            var track = GetOrCreateTrack(record, spotifyTrackId!, tracks, artists, albums);
            dbContext.ListeningEvents.Add(new ListeningEvent
            {
                User = user,
                Track = track,
                PlayedAtUtc = playedAtUtc,
                MsPlayed = (int)Math.Clamp(record.MsPlayed, 0, int.MaxValue),
                WasSkipped = record.Skipped,
                Shuffle = record.Shuffle,
                Offline = record.Offline,
                PrivateSession = record.IncognitoMode,
                Platform = Truncate(record.Platform, 200),
                StartReason = Truncate(record.ReasonStart, 100),
                EndReason = Truncate(record.ReasonEnd, 100),
                Source = HistorySource,
                ImportedAtUtc = DateTime.UtcNow,
                ExternalEventHash = externalHash,
            });
            batch.RecordsImported++;

            if (batch.RecordsImported % 1000 == 0) await dbContext.SaveChangesAsync(cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ImportLibraryFileAsync(
        SpotifyExportEntry entry,
        ImportBatch batch,
        User user,
        Dictionary<string, Track> tracks,
        Dictionary<string, Artist> artists,
        Dictionary<string, Album> albums,
        HashSet<long> savedTrackIds,
        CancellationToken cancellationToken)
    {
        await using var stream = await entry.OpenStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("tracks", out var trackArray) || trackArray.ValueKind != JsonValueKind.Array) return false;

        var recordNumber = 0;
        foreach (var element in trackArray.EnumerateArray())
        {
            recordNumber++;
            var uri = GetString(element, "uri");
            if (string.IsNullOrWhiteSpace(uri)) { AddError(batch, entry.FileName, recordNumber, "InvalidLibraryRecord", "Saved track has no URI."); continue; }
            string spotifyId;
            try { spotifyId = SpotifyTrackIdParser.Parse(uri); }
            catch (ArgumentException exception) { AddError(batch, entry.FileName, recordNumber, "InvalidLibraryUri", exception.Message); continue; }

            if (!tracks.TryGetValue(uri, out var track))
            {
                var record = new SpotifyHistoryRecord
                {
                    SpotifyTrackUri = uri,
                    TrackName = GetString(element, "track") ?? "Unknown Track",
                    ArtistName = GetString(element, "artist") ?? "Unknown Artist",
                    AlbumName = GetString(element, "album") ?? "Unknown Album",
                };
                track = GetOrCreateTrack(record, spotifyId, tracks, artists, albums);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            if (savedTrackIds.Add(track.Id)) dbContext.UserSavedTracks.Add(new UserSavedTrack { User = user, Track = track, SavedAtUtc = null, Source = LibrarySource, ImportedAtUtc = DateTime.UtcNow });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Track GetOrCreateTrack(SpotifyHistoryRecord record, string spotifyTrackId, Dictionary<string, Track> tracks, Dictionary<string, Artist> artists, Dictionary<string, Album> albums)
    {
        if (tracks.TryGetValue(record.SpotifyTrackUri!, out var existingTrack)) return existingTrack;
        var now = DateTime.UtcNow;
        var artistName = record.ArtistName?.Trim() ?? "Unknown Artist";
        var normalizedArtist = NameNormalizer.Normalize(artistName);
        if (!artists.TryGetValue(normalizedArtist, out var artist))
        {
            artist = new Artist { Name = artistName, NormalizedName = normalizedArtist, CreatedAtUtc = now, UpdatedAtUtc = now };
            artists.Add(normalizedArtist, artist);
            dbContext.Artists.Add(artist);
        }
        var albumName = record.AlbumName?.Trim() ?? "Unknown Album";
        var normalizedAlbum = NameNormalizer.Normalize(albumName);
        var albumKey = AlbumKey(normalizedAlbum, normalizedArtist);
        if (!albums.TryGetValue(albumKey, out var album))
        {
            album = new Album { Name = albumName, NormalizedName = normalizedAlbum, PrimaryArtist = artist, CreatedAtUtc = now, UpdatedAtUtc = now };
            albums.Add(albumKey, album);
            dbContext.Albums.Add(album);
        }
        var trackName = record.TrackName?.Trim() ?? "Unknown Track";
        var track = new Track
        {
            SpotifyTrackId = spotifyTrackId,
            SpotifyUri = record.SpotifyTrackUri,
            SpotifyUrl = $"https://open.spotify.com/track/{spotifyTrackId}",
            Name = trackName,
            NormalizedName = NameNormalizer.Normalize(trackName),
            Album = album,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        track.TrackArtists.Add(new TrackArtist { Track = track, Artist = artist, ArtistOrder = 0 });
        tracks.Add(record.SpotifyTrackUri!, track);
        dbContext.Tracks.Add(track);
        return track;
    }

    private async Task<ImportReport> CreateReportAsync(ImportBatch batch, User user, CancellationToken cancellationToken)
    {
        var events = dbContext.ListeningEvents.AsNoTracking().Where(x => x.UserId == user.Id);
        return new ImportReport(
            batch.Id,
            batch.RecordsFound,
            batch.RecordsImported,
            batch.Duplicates,
            batch.InvalidRecords,
            batch.IgnoredNonMusicRecords,
            await events.MinAsync(x => (DateTime?)x.PlayedAtUtc, cancellationToken),
            await events.MaxAsync(x => (DateTime?)x.PlayedAtUtc, cancellationToken),
            await events.Select(x => x.TrackId).Distinct().CountAsync(cancellationToken),
            await events.SelectMany(x => x.Track.TrackArtists.Select(ta => ta.ArtistId)).Distinct().CountAsync(cancellationToken),
            await events.Where(x => x.Track.AlbumId != null).Select(x => x.Track.AlbumId).Distinct().CountAsync(cancellationToken),
            user.SpotifyLibraryDataAvailable,
            await dbContext.UserSavedTracks.CountAsync(x => x.UserId == user.Id, cancellationToken));
    }

    private static bool TryValidate(SpotifyHistoryRecord record, out string? spotifyTrackId, out DateTime playedAtUtc, out string? error)
    {
        spotifyTrackId = null; playedAtUtc = default; error = null;
        try { spotifyTrackId = SpotifyTrackIdParser.Parse(record.SpotifyTrackUri!); }
        catch (ArgumentException exception) { error = exception.Message; return false; }
        if (!DateTimeOffset.TryParse(record.Timestamp, out var timestamp)) { error = "Invalid or missing timestamp."; return false; }
        if (record.MsPlayed < 0) { error = "ms_played cannot be negative."; return false; }
        if (string.IsNullOrWhiteSpace(record.TrackName) || string.IsNullOrWhiteSpace(record.ArtistName)) { error = "Track or artist name is missing."; return false; }
        playedAtUtc = timestamp.UtcDateTime;
        return true;
    }

    public static string CreateEventHash(long userId, SpotifyHistoryRecord record, DateTime playedAtUtc)
    {
        var payload = string.Join('\u001f', userId, playedAtUtc.ToString("O"), record.SpotifyTrackUri, record.MsPlayed, record.Platform, record.ReasonStart, record.ReasonEnd, record.Skipped, record.Shuffle, record.Offline, record.IncognitoMode);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string AlbumKey(string normalizedAlbum, string normalizedArtist) => $"{normalizedAlbum}\u001f{normalizedArtist}";
    private static string? Truncate(string? value, int maxLength) => value is null || value.Length <= maxLength ? value : value[..maxLength];
    private static string? GetString(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static void AddError(ImportBatch batch, string fileName, int recordNumber, string type, string message)
    {
        batch.InvalidRecords++;
        batch.Errors.Add(new ImportError { FileName = fileName, RecordNumber = recordNumber, ErrorType = type, ErrorMessage = Truncate(message, 2000)! });
    }
}

public sealed class SpotifyHistoryRecord
{
    [JsonPropertyName("ts")] public string? Timestamp { get; set; }
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("ms_played")] public long MsPlayed { get; set; }
    [JsonPropertyName("master_metadata_track_name")] public string? TrackName { get; set; }
    [JsonPropertyName("master_metadata_album_artist_name")] public string? ArtistName { get; set; }
    [JsonPropertyName("master_metadata_album_album_name")] public string? AlbumName { get; set; }
    [JsonPropertyName("spotify_track_uri")] public string? SpotifyTrackUri { get; set; }
    [JsonPropertyName("spotify_episode_uri")] public string? SpotifyEpisodeUri { get; set; }
    [JsonPropertyName("episode_name")] public string? EpisodeName { get; set; }
    [JsonPropertyName("reason_start")] public string? ReasonStart { get; set; }
    [JsonPropertyName("reason_end")] public string? ReasonEnd { get; set; }
    [JsonPropertyName("shuffle")] public bool? Shuffle { get; set; }
    [JsonPropertyName("skipped")] public bool? Skipped { get; set; }
    [JsonPropertyName("offline")] public bool? Offline { get; set; }
    [JsonPropertyName("incognito_mode")] public bool? IncognitoMode { get; set; }
}
