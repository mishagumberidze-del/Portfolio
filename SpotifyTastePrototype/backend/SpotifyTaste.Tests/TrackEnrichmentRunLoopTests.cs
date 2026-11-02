using SpotifyTaste.Application;

namespace SpotifyTaste.Tests;

public sealed class TrackEnrichmentRunLoopTests
{
    [Fact]
    public async Task RunAsync_continues_until_no_historical_tracks_remain()
    {
        var batches = new Queue<TrackEnrichmentBatchResult>(
        [
            new(3, 2, 1, 0, 2),
            new(2, 1, 1, 0, 0),
        ]);
        var delays = new List<TimeSpan>();
        using var output = new StringWriter();

        var result = await TrackEnrichmentRunLoop.RunAsync(
            _ => Task.FromResult(batches.Dequeue()),
            TimeSpan.FromSeconds(5),
            output,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.Equal(TrackEnrichmentStopReason.Completed, result.StopReason);
        Assert.Equal(2, result.Batches);
        Assert.Equal(5, result.Attempted);
        Assert.Equal(3, result.Completed);
        Assert.Equal(2, result.NotFound);
        Assert.Equal(0, result.RetryLater);
        Assert.Equal(0, result.RemainingEligible);
        Assert.Equal([TimeSpan.FromSeconds(5)], delays);
        Assert.Contains("Batch 2", output.ToString());
    }

    [Fact]
    public async Task RunAsync_stops_without_an_immediate_retry_when_a_batch_only_yields_retryable_failures()
    {
        var invocations = 0;
        var delays = new List<TimeSpan>();
        using var output = new StringWriter();

        var result = await TrackEnrichmentRunLoop.RunAsync(
            _ =>
            {
                invocations++;
                return Task.FromResult(new TrackEnrichmentBatchResult(3, 0, 0, 3, 3));
            },
            TimeSpan.FromSeconds(5),
            output,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.Equal(TrackEnrichmentStopReason.RetryLater, result.StopReason);
        Assert.Equal(1, invocations);
        Assert.Empty(delays);
        Assert.Contains("Stopped safely", output.ToString());
    }

    [Fact]
    public async Task RunAsync_uses_a_longer_backoff_after_a_partial_retryable_batch()
    {
        var batches = new Queue<TrackEnrichmentBatchResult>(
        [
            new(3, 2, 0, 1, 1),
            new(1, 1, 0, 0, 0),
        ]);
        var delays = new List<TimeSpan>();

        var result = await TrackEnrichmentRunLoop.RunAsync(
            _ => Task.FromResult(batches.Dequeue()),
            TimeSpan.FromSeconds(5),
            TextWriter.Null,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.Equal(TrackEnrichmentStopReason.Completed, result.StopReason);
        Assert.Equal([TimeSpan.FromSeconds(30)], delays);
    }
}
