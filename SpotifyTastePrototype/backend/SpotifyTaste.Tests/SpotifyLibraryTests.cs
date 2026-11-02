using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;
using SpotifyTaste.Infrastructure.Spotify;

namespace SpotifyTaste.Tests;

public sealed class SpotifyLibraryTests : IDisposable
{
    private readonly string archivePath = Path.Combine(Path.GetTempPath(), "SpotifyTasteTests-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(archivePath)) Directory.Delete(archivePath, recursive: true); }
    private static string Id(int n) => n.ToString().PadLeft(22, '0');
    private static SpotifySavedSong Song(int n) => new(Id(n), $"Song {n}", new DateTime(2026, 9, 1, 12, 30, 0, DateTimeKind.Utc), 200_000, null, Id(99), "Album", [new(Id(88), "Artist")]);
    private static object Item(int n, bool local = false, string? date = "2026-09-01T16:30:00+04:00") => new
    {
        added_at = date,
        track = new { id = Id(n), name = $"Song {n}", is_local = local, duration_ms = 200000,
            artists = new[] { new { id = Id(88), name = "Artist" } }, album = new { id = Id(99), name = "Album" } }
    };
    private static JsonDocument Page(int offset, int total, object[] items, string? next = null) => JsonDocument.Parse(JsonSerializer.Serialize(new { offset, total, items, next }));
    private static SpotifyTasteDbContext Db() => new(new DbContextOptionsBuilder<SpotifyTasteDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private SpotifyLibraryImportService Service(SpotifyTasteDbContext db, params SpotifySavedSong[] songs) => new(db, new LibraryStub(new(songs.Length, songs)), new(archivePath));

    [Fact]
    public void Pkce_matches_RFC7636_test_vector() => Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", SpotifyConnection.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void Authorization_requests_only_library_read_and_no_secret()
    {
        var c = Connection(new Handler(_ => throw new InvalidOperationException()));
        var a = c.Begin(); var b = c.Begin();
        Assert.Contains("scope=user-library-read", a.Url); Assert.Contains("code_challenge_method=S256", a.Url);
        Assert.DoesNotContain("client_secret", a.Url); Assert.NotEqual(a.BrowserBinding, b.BrowserBinding);
        Assert.DoesNotContain(a.BrowserBinding, a.Url); Assert.False(c.Connected);
    }

    [Fact]
    public async Task State_cookie_and_one_use_are_enforced()
    {
        var handler = new Handler(_ => Task.FromResult(TokenResponse())); var c = Connection(handler); var a = c.Begin(); var state = State(a);
        await Assert.ThrowsAsync<SpotifyConnectionException>(() => c.CompleteAsync(state, "wrong-browser", "code", null, default));
        await Assert.ThrowsAsync<SpotifyConnectionException>(() => c.CompleteAsync("wrong-state", a.BrowserBinding, "code", null, default));
        Assert.Equal(0, handler.Calls);
        await c.CompleteAsync(state, a.BrowserBinding, "code", null, default); Assert.True(c.Connected);
        await Assert.ThrowsAsync<SpotifyConnectionException>(() => c.CompleteAsync(state, a.BrowserBinding, "code", null, default));
        Assert.Equal(1, handler.Calls); await c.ForgetAsync(default); Assert.False(c.Connected);
    }

    [Fact]
    public async Task Denial_never_calls_token_endpoint()
    {
        var handler = new Handler(_ => throw new InvalidOperationException()); var c = Connection(handler); var a = c.Begin();
        await Assert.ThrowsAsync<SpotifyConnectionException>(() => c.CompleteAsync(State(a), a.BrowserBinding, null, "access_denied", default));
        Assert.Equal(0, handler.Calls); Assert.False(c.Connected);
    }

    [Fact]
    public async Task Missing_scope_does_not_establish_a_connection()
    {
        var c = Connection(new Handler(_ => Task.FromResult(TokenResponse(scope: "user-read-email")))); var a = c.Begin();
        await Assert.ThrowsAsync<SpotifyConnectionException>(() => c.CompleteAsync(State(a), a.BrowserBinding, "code", null, default));
        Assert.False(c.Connected);
    }

    [Fact]
    public async Task Refresh_reuses_refresh_token_when_not_rotated()
    {
        var bodies = new List<string>();
        var c = Connection(new Handler(async r => { bodies.Add(await r.Content!.ReadAsStringAsync()); return bodies.Count == 1 ? TokenResponse() : new(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"fresh\",\"expires_in\":3600}") }; }));
        var a = c.Begin(); await c.CompleteAsync(State(a), a.BrowserBinding, "code", null, default);
        Assert.Equal("fresh", await c.AccessTokenAsync(default, true)); Assert.Equal("fresh", await c.AccessTokenAsync(default, true));
        Assert.All(bodies, b => Assert.DoesNotContain("client_secret", b)); Assert.Contains("refresh_token=refresh", bodies[2]);
    }

    [Fact]
    public async Task All_pages_are_read_and_added_at_is_UTC()
    {
        var offsets = new List<int>();
        var result = await SpotifyLibraryClient.ReadPagesAsync((offset, _) =>
        {
            offsets.Add(offset);
            return Task.FromResult(offset == 0 ? Page(0, 51, Enumerable.Range(1, 50).Select(n => Item(n)).ToArray(), "https://untrusted.example/never-follow-this") : Page(50, 51, [Item(51)]));
        }, default);
        Assert.Equal(new[] { 0, 50 }, offsets); Assert.Equal(51, result.Total);
        Assert.Equal(DateTimeKind.Utc, result.Songs[0].SavedAtUtc!.Value.Kind); Assert.Equal(12, result.Songs[0].SavedAtUtc!.Value.Hour);
    }

    [Theory]
    [InlineData(2, 1)] [InlineData(0, 1)] [InlineData(100001, 0)]
    public async Task Inconsistent_page_counts_rejected(int total, int rows) =>
        await Assert.ThrowsAsync<SpotifyConnectionException>(() => SpotifyLibraryClient.ReadPagesAsync((_, _) => Task.FromResult(Page(0, total, Enumerable.Range(1, rows).Select(n => Item(n)).ToArray())), default));

    [Fact]
    public async Task Duplicate_saved_id_rejected() => await Assert.ThrowsAsync<SpotifyConnectionException>(() => SpotifyLibraryClient.ReadPagesAsync((_, _) => Task.FromResult(Page(0, 2, [Item(1), Item(1)])), default));

    [Fact]
    public async Task Changed_total_between_pages_rejected() => await Assert.ThrowsAsync<SpotifyConnectionException>(() => SpotifyLibraryClient.ReadPagesAsync((offset, _) => Task.FromResult(offset == 0 ? Page(0, 51, Enumerable.Range(1, 50).Select(n => Item(n)).ToArray(), "next") : Page(50, 52, [Item(51), Item(52)])), default));

    [Theory]
    [InlineData("null")] [InlineData("{\"track\":null}")]
    public void Unavailable_track_rejected(string json)
    {
        using var item = JsonDocument.Parse(json);
        Assert.ThrowsAny<Exception>(() => SpotifyLibraryClient.ParseSong(item.RootElement));
    }

    [Fact]
    public void Local_track_rejected()
    {
        using var item = JsonDocument.Parse(JsonSerializer.Serialize(Item(1, local: true)));
        Assert.Throws<SpotifyConnectionException>(() => SpotifyLibraryClient.ParseSong(item.RootElement));
    }

    [Fact]
    public void Missing_saved_date_stays_null()
    {
        using var item = JsonDocument.Parse(JsonSerializer.Serialize(Item(1, date: null)));
        Assert.Null(SpotifyLibraryClient.ParseSong(item.RootElement).SavedAtUtc);
    }

    [Fact]
    public void Relink_keeps_original_saved_identity()
    {
        using var item = JsonDocument.Parse(JsonSerializer.Serialize(new { track = new { id = Id(2), linked_from = new { id = Id(1) }, name = "Song", artists = new[] { new { id = Id(88), name = "Artist" } } } }));
        Assert.Equal(Id(1), SpotifyLibraryClient.ParseSong(item.RootElement).Id);
    }

    [Fact]
    public async Task Import_is_idempotent_and_does_not_change_history_or_existing_metadata()
    {
        await using var db = Db(); var user = new User(); var t = new Track { SpotifyTrackId = Id(1), Name = "Original history name" };
        db.ListeningEvents.Add(new ListeningEvent { User = user, Track = t, MsPlayed = 120000 }); await db.SaveChangesAsync();
        var s = Service(db, Song(1), Song(2)); var first = await s.ImportAsync(default); var second = await s.ImportAsync(default);
        Assert.Equal(2, first.Added); Assert.Equal(1, first.NewTracks); Assert.Equal(0, second.Added); Assert.Equal(2, second.Updated);
        Assert.Equal(2, await db.UserSavedTracks.CountAsync()); Assert.Equal(1, await db.ListeningEvents.CountAsync()); Assert.Equal(120000, await db.ListeningEvents.SumAsync(e => e.MsPlayed));
        Assert.Equal("Original history name", t.Name); Assert.True(user.SpotifyLibraryDataAvailable);
        Assert.All(await db.UserSavedTracks.ToListAsync(), row => { Assert.Equal(SpotifyLibraryImportService.Source, row.Source); Assert.Equal(Song(1).SavedAtUtc, row.SavedAtUtc); });
        Assert.Equal(2, Directory.GetFiles(archivePath).Length);
    }

    [Fact]
    public async Task Complete_snapshot_replaces_stale_saves_and_archives_previous_snapshot()
    {
        await using var db = Db(); db.Users.Add(new User()); await db.SaveChangesAsync();
        await Service(db, Song(1), Song(2)).ImportAsync(default);
        var result = await Service(db, Song(2), Song(3)).ImportAsync(default);
        Assert.Equal(1, result.Removed); Assert.Equal(1, result.Added);
        Assert.Equal(new[] { Id(2), Id(3) }, await db.UserSavedTracks.OrderBy(s => s.Track.SpotifyTrackId).Select(s => s.Track.SpotifyTrackId).ToArrayAsync());
        Assert.Equal(3, await db.Tracks.CountAsync()); Assert.Empty(db.ListeningEvents);
        var before = Directory.GetFiles(archivePath).Select(File.ReadAllText).Single(json => json != "[]");
        Assert.Equal(2, JsonDocument.Parse(before).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Download_failure_preserves_previous_library_and_logs_safe_error()
    {
        await using var db = Db(); db.Users.Add(new User()); await db.SaveChangesAsync(); await Service(db, Song(1)).ImportAsync(default);
        var s = new SpotifyLibraryImportService(db, new LibraryStub(null), new(archivePath));
        await Assert.ThrowsAsync<HttpRequestException>(() => s.ImportAsync(default));
        Assert.Single(db.UserSavedTracks); Assert.True(await db.Users.Select(u => u.SpotifyLibraryDataAvailable).SingleAsync());
        Assert.Equal(ImportBatchStatus.Failed, (await db.ImportBatches.OrderBy(b => b.Id).LastAsync()).Status);
        Assert.DoesNotContain("sensitive", (await db.ImportErrors.SingleAsync()).ErrorMessage);
    }

    [Fact]
    public async Task Empty_complete_library_is_known_empty_not_unknown()
    {
        await using var db = Db(); db.Users.Add(new User()); await db.SaveChangesAsync(); await Service(db, Song(1)).ImportAsync(default);
        var result = await Service(db).ImportAsync(default);
        Assert.Equal(0, result.SavedTracks); Assert.Equal(1, result.Removed); Assert.True(await db.Users.Select(u => u.SpotifyLibraryDataAvailable).SingleAsync());
    }

    private sealed class LibraryStub(SpotifyLibrarySnapshot? snapshot) : ISpotifyLibraryClient
    {
        public Task<SpotifyLibrarySnapshot> ReadAllAsync(CancellationToken ct) => snapshot is null ? throw new HttpRequestException("sensitive provider body") : Task.FromResult(snapshot);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return send(request); }
    }
    private static SpotifyConnection Connection(Handler h) => new(new HttpClient(h), Options.Create(new SpotifyConnectionOptions { ClientId = new string('a', 32) }));
    private static HttpResponseMessage TokenResponse(string scope = "user-library-read") => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { access_token = "access", refresh_token = "refresh", expires_in = 3600, scope })) };
    private static string State(SpotifyAuthorization a) => Uri.UnescapeDataString(new Uri(a.Url).Query.TrimStart('?').Split('&').Single(p => p.StartsWith("state=")).Split('=')[1]);
}
