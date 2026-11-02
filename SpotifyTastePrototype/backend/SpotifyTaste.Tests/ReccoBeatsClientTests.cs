using System.Net;
using System.Text;
using SpotifyTaste.Infrastructure.ReccoBeats;

namespace SpotifyTaste.Tests;

public sealed class ReccoBeatsClientTests
{
    [Fact]
    public async Task Spotify_mapping_and_audio_features_are_combined()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("audio-features")
            ? "{\"tempo\":120.5,\"energy\":0.8,\"danceability\":0.7,\"valence\":0.6}"
            : "{\"content\":[{\"id\":\"recco-id\",\"trackTitle\":\"Mapped Track\",\"artists\":[{\"id\":\"a\",\"name\":\"Artist\",\"href\":null}],\"durationMs\":200000,\"isrc\":\"TEST\",\"href\":\"https://open.spotify.com/track/6fOqEAXCwboCO4BdzbZG1K\"}]}" );
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.reccobeats.com/") };

        var track = await new ReccoBeatsClient(http).GetBySpotifyIdAsync("6fOqEAXCwboCO4BdzbZG1K");

        Assert.NotNull(track);
        Assert.Equal("6fOqEAXCwboCO4BdzbZG1K", track.SpotifyTrackId);
        Assert.Equal(120.5, track.Features!.Tempo);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Large_candidate_pool_is_split_at_the_live_provider_limit()
    {
        var call = 0;
        var handler = new StubHandler(request =>
        {
            call++;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            Assert.InRange(int.Parse(query["size"]!), 1, 100);
            var spotifyId = call == 1 ? "0000000000000000000001" : "0000000000000000000002";
            return $"{{\"content\":[{{\"id\":\"recco-{call}\",\"trackTitle\":\"Candidate {call}\",\"artists\":[{{\"id\":\"a\",\"name\":\"Artist {call}\",\"href\":null}}],\"durationMs\":200000,\"isrc\":null,\"href\":\"https://open.spotify.com/track/{spotifyId}\"}}]}}";
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.reccobeats.com/") };

        var tracks = await new ReccoBeatsClient(http).GetRecommendationsAsync(
            Enumerable.Range(0, 10).Select(x => $"seed-{x}").ToArray(), [], 150);

        Assert.Equal(2, handler.CallCount);
        Assert.Equal(2, tracks.Count);
    }

    [Fact]
    public async Task Rate_limit_is_retried_using_retry_after()
    {
        var call = 0;
        var handler = new ResponseHandler(_ =>
        {
            call++;
            if (call == 1)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return limited;
            }
            var json = call == 2
                ? "{\"content\":[{\"id\":\"recco-id\",\"trackTitle\":\"Track\",\"artists\":[],\"durationMs\":1,\"isrc\":null,\"href\":\"https://open.spotify.com/track/6fOqEAXCwboCO4BdzbZG1K\"}]}"
                : "{\"energy\":0.5}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.reccobeats.com/") };

        Assert.NotNull(await new ReccoBeatsClient(http).GetBySpotifyIdAsync("6fOqEAXCwboCO4BdzbZG1K"));
        Assert.Equal(3, call);
    }

    [Fact]
    public async Task Not_found_mapping_is_returned_as_null()
    {
        var http = new HttpClient(new ResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))) { BaseAddress = new Uri("https://api.reccobeats.com/") };
        Assert.Null(await new ReccoBeatsClient(http).GetBySpotifyIdAsync("6fOqEAXCwboCO4BdzbZG1K"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
        }
    }

    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
