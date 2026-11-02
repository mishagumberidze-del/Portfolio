using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SpotifyTaste.Infrastructure.Spotify;

public sealed class SpotifyConnectionOptions
{
    public string ClientId { get; set; } = "";
    public const string Callback = "http://127.0.0.1:5143/api/spotify/callback";
    public const string Frontend = "http://127.0.0.1:5173/";
    public const string Scope = "user-library-read";
}

public sealed class SpotifyConnectionException(string message) : Exception(message);
public sealed record SpotifyAuthorization(string Url, string BrowserBinding);

// Single-local-user session. Secrets never leave process memory or appear in DTOs/logs.
// Restarting the API requires reconnecting; SQL library snapshots survive a restart.
public sealed class SpotifyConnection(HttpClient http, IOptions<SpotifyConnectionOptions> options)
{
    private readonly object sync = new();
    private readonly SemaphoreSlim tokenGate = new(1, 1);
    private Pending? pending;
    private Token? token;
    private sealed record Pending(string State, string Binding, string Verifier, DateTimeOffset Expires);
    private sealed record Token(string Access, string? Refresh, DateTimeOffset Expires);
    public bool Configured => options.Value.ClientId.Length == 32 && options.Value.ClientId.All(Uri.IsHexDigit);
    public bool Connected { get { lock (sync) return token is { } t && (t.Refresh is not null || t.Expires > DateTimeOffset.UtcNow); } }

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Random() => Base64Url(RandomNumberGenerator.GetBytes(32));
    private static bool Equal(string? a, string b) => a is not null && a.Length == b.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    public SpotifyAuthorization Begin()
    {
        if (!Configured) throw new SpotifyConnectionException("Configure your Spotify Client ID in appsettings.Local.json first. A client secret is not needed.");
        var p = new Pending(Random(), Random(), Random(), DateTimeOffset.UtcNow.AddMinutes(10));
        lock (sync) pending = p;
        var query = new Dictionary<string, string>
        {
            ["client_id"] = options.Value.ClientId, ["response_type"] = "code",
            ["redirect_uri"] = SpotifyConnectionOptions.Callback, ["scope"] = SpotifyConnectionOptions.Scope,
            ["state"] = p.State, ["code_challenge_method"] = "S256", ["code_challenge"] = Challenge(p.Verifier),
            ["show_dialog"] = "true"
        };
        return new("https://accounts.spotify.com/authorize?" + string.Join('&', query.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}")), p.Binding);
    }

    public async Task CompleteAsync(string? state, string? binding, string? code, string? error, CancellationToken ct)
    {
        Pending p;
        lock (sync)
        {
            if (pending is not { } current || current.Expires < DateTimeOffset.UtcNow || !Equal(state, current.State) || !Equal(binding, current.Binding))
                throw new SpotifyConnectionException("Spotify connection expired or the browser did not match. Start Connect Spotify again in this browser.");
            p = current;
            pending = null; // One use, including denial/error callbacks.
        }
        if (error is not null || string.IsNullOrWhiteSpace(code)) throw new SpotifyConnectionException("Spotify access was not approved. No library was imported.");
        await tokenGate.WaitAsync(ct);
        try
        {
            var granted = await ExchangeAsync(new()
            {
                ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = SpotifyConnectionOptions.Callback,
                ["client_id"] = options.Value.ClientId, ["code_verifier"] = p.Verifier
            }, null, ct);
            lock (sync) token = granted;
        }
        finally { tokenGate.Release(); }
    }

    public async Task ForgetAsync(CancellationToken ct)
    {
        await tokenGate.WaitAsync(ct);
        try { lock (sync) { token = null; pending = null; } }
        finally { tokenGate.Release(); }
    }

    public async Task<string> AccessTokenAsync(CancellationToken ct, bool forceRefresh = false)
    {
        await tokenGate.WaitAsync(ct);
        try
        {
            Token t;
            lock (sync) t = token ?? throw new SpotifyConnectionException("Connect Spotify before importing your Liked Songs.");
            if (!forceRefresh && t.Expires > DateTimeOffset.UtcNow.AddSeconds(60)) return t.Access;
            if (t.Refresh is null) throw new SpotifyConnectionException("Spotify authorization expired. Connect again.");
            try
            {
                var refreshed = await ExchangeAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = t.Refresh, ["client_id"] = options.Value.ClientId }, t.Refresh, ct);
                lock (sync) token = refreshed;
                return refreshed.Access;
            }
            catch (SpotifyConnectionException) { lock (sync) token = null; throw; }
        }
        finally { tokenGate.Release(); }
    }

    private async Task<Token> ExchangeAsync(Dictionary<string, string> fields, string? oldRefresh, CancellationToken ct)
    {
        using var response = await http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(fields), ct);
        if (!response.IsSuccessStatusCode) throw new SpotifyConnectionException($"Spotify authorization failed (HTTP {(int)response.StatusCode}). Check your Client ID and registered Redirect URI, then reconnect.");
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = json.RootElement;
        var scope = root.TryGetProperty("scope", out var s) ? s.GetString() : null;
        if ((oldRefresh is null || scope is not null) && !(scope ?? "").Split(' ').Contains(SpotifyConnectionOptions.Scope))
            throw new SpotifyConnectionException("Spotify did not grant user-library-read. Reconnect and approve saved-library access.");
        if (!root.TryGetProperty("access_token", out var a) || string.IsNullOrWhiteSpace(a.GetString()) || !root.TryGetProperty("expires_in", out var e) || !e.TryGetInt32(out var seconds) || seconds <= 0)
            throw new SpotifyConnectionException("Spotify returned an incomplete authorization response. Reconnect.");
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : oldRefresh;
        return new(a.GetString()!, refresh, DateTimeOffset.UtcNow.AddSeconds(seconds));
    }
}

public sealed class SpotifyLibraryWorkGate
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}
