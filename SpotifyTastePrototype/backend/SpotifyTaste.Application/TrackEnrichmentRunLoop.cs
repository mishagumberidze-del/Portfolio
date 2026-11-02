namespace SpotifyTaste.Application;

public enum TrackEnrichmentStopReason
{
    Completed,
    RetryLater,
}

public sealed record TrackEnrichmentRunResult(
    int Batches,
    int Attempted,
    int Completed,
    int NotFound,
    int RetryLater,
    int RemainingEligible,
    TrackEnrichmentStopReason StopReason);

public static class TrackEnrichmentRunLoop
{
    private static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(30);

    public static async Task<TrackEnrichmentRunResult> RunAsync(
        Func<CancellationToken, Task<TrackEnrichmentBatchResult>> enrichBatch,
        TimeSpan interBatchDelay,
        TextWriter output,
        CancellationToken cancellationToken = default,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        ArgumentNullException.ThrowIfNull(enrichBatch);
        ArgumentNullException.ThrowIfNull(output);
        if (interBatchDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interBatchDelay), "The inter-batch delay cannot be negative.");

        delayAsync ??= Task.Delay;
        var batches = 0;
        var attempted = 0;
        var completed = 0;
        var notFound = 0;
        var retryLater = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = await enrichBatch(cancellationToken);
            batches++;
            attempted += batch.Attempted;
            completed += batch.Completed;
            notFound += batch.NotFound;
            retryLater += batch.RetryLater;

            await output.WriteLineAsync(
                $"Batch {batches}: attempted {batch.Attempted}, cached {batch.Completed}, " +
                $"not found {batch.NotFound}, retry later {batch.RetryLater}, remaining {batch.RemainingEligible}.");

            if (batch.Attempted == 0 || batch.RemainingEligible == 0)
                return new TrackEnrichmentRunResult(batches, attempted, completed, notFound, retryLater, batch.RemainingEligible, TrackEnrichmentStopReason.Completed);

            if (batch.Completed == 0 && batch.RetryLater > 0)
            {
                await output.WriteLineAsync(
                    "Stopped safely because this batch produced no cached features and the provider requested retries. " +
                    "Wait before resuming the same command; completed earlier batches are already stored in SQL Server.");
                return new TrackEnrichmentRunResult(batches, attempted, completed, notFound, retryLater, batch.RemainingEligible, TrackEnrichmentStopReason.RetryLater);
            }

            var delay = batch.RetryLater > 0 && interBatchDelay < RetryBackoff ? RetryBackoff : interBatchDelay;
            if (delay > TimeSpan.Zero)
            {
                await output.WriteLineAsync($"Waiting {delay.TotalSeconds:0} second(s) before the next batch...");
                await delayAsync(delay, cancellationToken);
            }
        }
    }
}
