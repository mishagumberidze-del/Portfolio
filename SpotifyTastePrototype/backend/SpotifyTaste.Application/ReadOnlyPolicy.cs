namespace SpotifyTaste.Application;
public static class ReadOnlyPolicy
{
    public static bool Allows(string method, string path) =>
        (method is "GET" or "HEAD" or "OPTIONS") &&
        !path.StartsWith("/api/spotify", StringComparison.OrdinalIgnoreCase);
}
