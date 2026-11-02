using Microsoft.EntityFrameworkCore;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;

namespace SpotifyTaste.Infrastructure.Recommendations;

// Shared by HTTP generation/evaluation endpoints to prevent simultaneous runs
// from selecting the same previously unseen pool on this single-user process.
public sealed class RecommendationWorkGate
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}

public sealed class FeedbackService(SpotifyTasteDbContext db, IRecommendationService recommendations)
{
    private static readonly HashSet<string> Kinds = ["Love", "Like", "NotForMe", "AlreadyKnow", "NotInterested", "Rating"];
    public async Task<FeedbackDto> AddAsync(FeedbackRequest request, CancellationToken ct = default)
    {
        if (!Kinds.Contains(request.Kind) || (request.Kind == "Rating" ? request.Rating is not (>= 1 and <= 5) : request.Rating is not null))
            throw new ArgumentException("Choose a valid feedback label, or a rating between 1 and 5.");
        if (!await db.RecommendationCandidates.AnyAsync(c => c.Id == request.CandidateId, ct)) throw new KeyNotFoundException("Recommendation not found.");
        if (request.EvaluationItemId.HasValue)
        {
            if (request.Kind is not ("Rating" or "AlreadyKnow")) throw new ArgumentException("Evaluation accepts a 1–5 rating or Already know it.");
            if (!await db.EvaluationItems.AnyAsync(i => i.Id == request.EvaluationItemId && i.RecommendationCandidateId == request.CandidateId, ct))
                throw new ArgumentException("This recommendation does not belong to the evaluation item.");
        }
        else if (request.Kind == "Rating") throw new ArgumentException("Numeric ratings require an evaluation item.");
        var row = new RecommendationFeedback { RecommendationCandidateId = request.CandidateId, EvaluationItemId = request.EvaluationItemId,
            Kind = request.Kind, Rating = request.Rating, CreatedAtUtc = DateTime.UtcNow };
        db.RecommendationFeedback.Add(row); await db.SaveChangesAsync(ct);
        return new(row.Id, row.Kind, row.Rating, row.CreatedAtUtc);
    }

    public async Task<DiscoverDto> LatestAsync(CancellationToken ct = default)
    {
        var library = await db.Users.Select(u => u.SpotifyLibraryDataAvailable).SingleOrDefaultAsync(ct);
        var latest = await db.RecommendationRuns.AsNoTracking().GroupBy(r => r.ListType).Select(g => g.Max(r => r.Id)).ToArrayAsync(ct);
        var candidates = await Candidates().Where(c => latest.Contains(c.RecommendationRunId)).Include(c => c.RecommendationRun).OrderBy(c => c.Rank).ToListAsync(ct);
        var ids = candidates.Select(c => c.Id).ToArray(); var feedback = await LatestFeedbackAsync(ids, ct);
        var history = await HistoryIdentitiesAsync(ct);
        var never = candidates.Where(c => c.RecommendationRun.ListType == RecommendationListType.NeverHeard && !IsKnown(c.Track, history)).Select(c => ToDto(c, feedback.GetValueOrDefault(c.Id))).ToArray();
        // Re-evaluate library availability rather than leaking a historical Not Saved list.
        var saved = library ? await IdentitiesAsync(db.UserSavedTracks.Select(s => s.TrackId).Distinct(), ct) : [];
        var notSaved = library ? candidates.Where(c => c.RecommendationRun.ListType == RecommendationListType.NotSaved && !IsKnown(c.Track, saved))
            .Select(c => ToDto(c, feedback.GetValueOrDefault(c.Id)) with { HistoricalStatus = IsKnown(c.Track, history) ? "Heard But Not Saved" : "Never Heard" }).ToArray() : [];
        return new(library, library ? null : "Spotify saved/liked library data was not found in the imported package.", notSaved, never);
    }

    public async Task<EvaluationDto?> GetAsync(CancellationToken ct = default)
    {
        var session = await db.EvaluationSessions.AsNoTracking().OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);
        return session is null ? null : await ReadSessionAsync(session, ct);
    }
    public async Task<EvaluationDto> StartAsync(int target, CancellationToken ct = default)
    {
        if (target is < 50 or > 100) throw new ArgumentException("Choose a target between 50 and 100.");
        // Idempotent resume: do not silently abandon the owner's existing labels.
        var existing = await GetAsync(ct); if (existing is not null) return existing;
        var session = new EvaluationSession { TargetCount = target, CreatedAtUtc = DateTime.UtcNow };
        db.EvaluationSessions.Add(session); await db.SaveChangesAsync(ct);
        return await ReadSessionAsync(session, ct);
    }
    public async Task<EvaluationDto> NextAsync(CancellationToken ct = default)
    {
        var current = await GetAsync(ct) ?? throw new ArgumentException("Start an evaluation first.");
        if (current.Items.Any(i => !i.AlreadyKnown && !i.Rating.HasValue)) throw new ArgumentException("Rate the current batch or mark tracks already known before fetching another batch.");
        var needed = current.TargetCount - current.Items.Count(i => !i.AlreadyKnown);
        if (needed <= 0) return current;
        var batch = await recommendations.GenerateAsync(Math.Min(10, needed), ct);
        var position = current.Items.Count;
        foreach (var candidate in batch.NeverHeard)
            db.EvaluationItems.Add(new EvaluationItem { EvaluationSessionId = current.Id, RecommendationCandidateId = candidate.CandidateId, Position = ++position });
        await db.SaveChangesAsync(ct);
        return (await GetAsync(ct))!;
    }

    private async Task<EvaluationDto> ReadSessionAsync(EvaluationSession session, CancellationToken ct)
    {
        var items = await db.EvaluationItems.AsNoTracking().Where(i => i.EvaluationSessionId == session.Id).OrderBy(i => i.Position).ToListAsync(ct);
        var ids = items.Select(i => i.RecommendationCandidateId).ToArray();
        var candidates = await Candidates().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var itemIds = items.Select(i => i.Id).ToArray();
        var labels = (await db.RecommendationFeedback.AsNoTracking().Where(f => f.EvaluationItemId != null && itemIds.Contains(f.EvaluationItemId.Value)).ToListAsync(ct))
            .GroupBy(f => f.EvaluationItemId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.Id).First());
        var allLabels = await LatestFeedbackAsync(ids, ct);
        var history = await HistoryIdentitiesAsync(ct);
        var entries = items.Select(i => { var label = labels.GetValueOrDefault(i.Id); var candidate = candidates[i.RecommendationCandidateId];
            return new EvaluationEntry(i.Id, i.Position, ToDto(candidate, label?.Kind), label?.Rating,
                label?.Kind == "AlreadyKnow" || allLabels.GetValueOrDefault(candidate.Id) == "AlreadyKnow" || IsKnown(candidate.Track, history)); }).ToArray();
        return new(session.Id, session.TargetCount, session.CreatedAtUtc, entries,
            EvaluationMath.Calculate(entries.Select(i => new EvaluationRating(i.Position, i.Recommendation.Band, i.Rating, i.AlreadyKnown)).ToArray()));
    }
    private IQueryable<RecommendationCandidate> Candidates() => db.RecommendationCandidates.AsNoTracking()
        .Include(c => c.Track).ThenInclude(t => t.Album).Include(c => c.Track).ThenInclude(t => t.TrackArtists).ThenInclude(a => a.Artist).AsSplitQuery();
    private async Task<Dictionary<long, string>> LatestFeedbackAsync(long[] ids, CancellationToken ct) =>
        (await db.RecommendationFeedback.AsNoTracking().Where(f => ids.Contains(f.RecommendationCandidateId)).ToListAsync(ct))
        .GroupBy(f => f.RecommendationCandidateId).ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.Id).First().Kind);
    private Task<RecordingIdentityInput[]> HistoryIdentitiesAsync(CancellationToken ct) => IdentitiesAsync(db.ListeningEvents.Select(e => e.TrackId).Distinct(), ct);
    private async Task<RecordingIdentityInput[]> IdentitiesAsync(IQueryable<long> ids, CancellationToken ct)
    {
        var rows = await db.Tracks.AsNoTracking().Where(t => ids.Contains(t.Id)).Include(t => t.Album).Include(t => t.TrackArtists).ThenInclude(a => a.Artist).AsSplitQuery().ToListAsync(ct);
        return rows.Select(Identity).ToArray();
    }
    private static bool IsKnown(Track track, RecordingIdentityInput[] history)
    {
        var identity = Identity(track);
        return history.Any(h => RecordingIdentityResolver.AreEquivalent(h, identity));
    }
    private static RecordingIdentityInput Identity(Track t) => new(t.Isrc, t.SpotifyTrackId, t.Name, t.TrackArtists.OrderBy(a => a.ArtistOrder).Select(a => a.Artist.Name).ToArray(), t.Album?.Name, t.DurationMs);
    public static RecommendationDto ToDto(RecommendationCandidate c, string? feedback = null) => new(c.Rank, c.Track.Name,
        string.Join(", ", c.Track.TrackArtists.OrderBy(a => a.ArtistOrder).Select(a => a.Artist.Name)), c.ArtworkUrl ?? c.Track.Album?.ImageUrl,
        (double)c.FinalScore, c.Confidence, c.Explanation, c.NoveltyScore > 0 ? "Never Heard" : "Heard But Not Saved",
        c.Track.SpotifyUrl ?? (c.Track.SpotifyTrackId is null ? null : $"https://open.spotify.com/track/{c.Track.SpotifyTrackId}"), c.Band.ToString(), c.Id, c.AlbumName ?? c.Track.Album?.Name, feedback);
}
