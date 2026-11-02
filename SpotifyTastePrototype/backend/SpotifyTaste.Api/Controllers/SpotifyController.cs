using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;
using SpotifyTaste.Infrastructure.Spotify;

namespace SpotifyTaste.Api.Controllers;

[ApiController]
[Route("api/spotify")]
public sealed class SpotifyController(SpotifyConnection connection, SpotifyLibraryImportService importer, SpotifyLibraryWorkGate work, SpotifyTasteDbContext db, IOptions<PreferenceModelOptions> modelOptions) : Controller
{
    private const string BindingCookie = "spotify-taste-oauth";
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var remote = HttpContext.Connection.RemoteIpAddress;
        var host = Request.Host.Host;
        // Loopback binding plus host checking also rejects DNS-rebinding targets.
        if (remote is null || !IPAddress.IsLoopback(remote) || host is not ("127.0.0.1" or "localhost" or "[::1]"))
        { context.Result = StatusCode(403, "Spotify connection is available only on this computer."); return; }
        if (HttpMethods.IsPost(Request.Method) && (Request.Headers["X-Spotify-Local"] != "1" || Request.Headers.Origin.ToString() is not ("http://127.0.0.1:5173" or "http://localhost:5173")))
            context.Result = StatusCode(403, "Use the Spotify connection controls in your local dashboard.");
    }

    [HttpGet("status")]
    public async Task<object> Status(CancellationToken ct) => new
    {
        configured = connection.Configured, connected = connection.Connected,
        redirectUri = SpotifyConnectionOptions.Callback, scope = SpotifyConnectionOptions.Scope,
        savedLibraryAvailable = await db.Users.Select(u => u.SpotifyLibraryDataAvailable).SingleAsync(ct),
        savedTrackCount = await db.UserSavedTracks.CountAsync(ct),
        savedTracksWithFeatures = await db.UserSavedTracks.CountAsync(s => db.TrackFeatures.Any(f => f.TrackId == s.TrackId && f.Provider == "ReccoBeats"), ct),
        savedTracksNotFound = await db.UserSavedTracks.CountAsync(s => s.Track.EnrichmentStatus == EnrichmentStatus.NotFound && !db.TrackFeatures.Any(f => f.TrackId == s.TrackId && f.Provider == "ReccoBeats"), ct),
        savedTracksPending = await db.UserSavedTracks.CountAsync(s => s.Track.EnrichmentStatus == EnrichmentStatus.Pending || s.Track.EnrichmentStatus == EnrichmentStatus.RetryLater, ct),
        preferenceModelVersion = modelOptions.Value.ModelVersion,
        savedBoostedHistoricalEntries = await db.TrackPreferences.CountAsync(p => p.ModelVersion == modelOptions.Value.ModelVersion && p.SavedBoost > 0, ct),
        lastModelRebuildAtUtc = await db.TrackPreferences.Where(p => p.ModelVersion == modelOptions.Value.ModelVersion).MaxAsync(p => (DateTime?)p.CalculatedAtUtc, ct),
        lastImport = await db.ImportBatches.Where(b => b.Source == SpotifyLibraryImportService.Source).OrderByDescending(b => b.Id)
            .Select(b => new { b.Id, b.RecordsFound, b.RecordsImported, b.CompletedAtUtc, status = b.Status.ToString() }).FirstOrDefaultAsync(ct)
    };

    [HttpPost("connect")]
    public async Task<IActionResult> Connect(CancellationToken ct)
    {
        if (!await work.Gate.WaitAsync(0, ct)) return Conflict("A library import is running. Wait for it to finish.");
        try
        {
            var auth = connection.Begin();
            Response.Cookies.Append(BindingCookie, auth.BrowserBinding, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/api/spotify", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true });
            return Ok(new { authorizationUrl = auth.Url });
        }
        catch (SpotifyConnectionException ex) { return BadRequest(ex.Message); }
        finally { work.Gate.Release(); }
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string? state, string? code, string? error, CancellationToken ct)
    {
        if (!await work.Gate.WaitAsync(0, ct)) return Conflict("A library import is running. Start the Spotify connection again after it finishes.");
        try
        {
            await connection.CompleteAsync(state, Request.Cookies[BindingCookie], code, error, ct);
            return Redirect(SpotifyConnectionOptions.Frontend + "?spotify=connected");
        }
        catch (Exception ex) when (ex is SpotifyConnectionException or HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        { return Redirect(SpotifyConnectionOptions.Frontend + "?spotify=failed"); }
        finally { Response.Cookies.Delete(BindingCookie, new CookieOptions { Path = "/api/spotify" }); work.Gate.Release(); }
    }

    [HttpPost("import-liked")]
    public async Task<IActionResult> Import(CancellationToken ct)
    {
        if (!connection.Connected) return Conflict("Connect Spotify first.");
        if (!await work.Gate.WaitAsync(0, ct)) return Conflict("A Spotify operation is already running.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try { return Ok(await importer.ImportAsync(timeout.Token)); }
        catch (SpotifyConnectionException ex) { return BadRequest(ex.Message); }
        catch (OperationCanceledException) { return StatusCode(408, "Import cancelled or timed out. The previous SQL snapshot is unchanged."); }
        catch (Exception) { return StatusCode(502, "Library import failed. Previous saved state is unchanged. See the import batch in Diagnostics and retry."); }
        finally { work.Gate.Release(); }
    }

    [HttpPost("end-session")]
    public async Task<IActionResult> EndSession(CancellationToken ct)
    {
        if (!await work.Gate.WaitAsync(0, ct)) return Conflict("Wait for the current library import to finish.");
        try { await connection.ForgetAsync(ct); return Ok(new { connected = false }); }
        finally { work.Gate.Release(); }
    }
}
