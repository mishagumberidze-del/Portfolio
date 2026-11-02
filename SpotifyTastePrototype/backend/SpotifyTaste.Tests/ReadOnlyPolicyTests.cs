using SpotifyTaste.Application;
namespace SpotifyTaste.Tests;
public class ReadOnlyPolicyTests
{
    [Theory]
    [InlineData("GET", "/api/story", true)]
    [InlineData("HEAD", "/api/story", true)]
    [InlineData("OPTIONS", "/api/story", true)]
    [InlineData("POST", "/api/model/rebuild", false)]
    [InlineData("POST", "/api/scorer", false)]
    [InlineData("POST", "/api/feedback", false)]
    [InlineData("DELETE", "/api/story", false)]
    [InlineData("GET", "/api/spotify/callback", false)]
    public void ReadOnlyEditionNeverTriggersMutations(string method, string path, bool allowed) =>
        Assert.Equal(allowed, ReadOnlyPolicy.Allows(method, path));
}
