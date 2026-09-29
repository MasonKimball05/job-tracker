using System.Collections.Concurrent;
using JobTracker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobTracker.Services.Radar;

/// <summary>
/// One pass of the radar: fetch boards → store new postings → free filter →
/// submit a Claude batch → collect finished batches → notify strong matches.
/// </summary>
public class RadarEngine(
    IDbContextFactory<JobDbContext> dbFactory,
    IJobBoards boards,
    IRadarScorer scorer,
    IRadarNotifier notifier,
    IOptionsMonitor<RadarOptions> options,
    TimeProvider time,
    ILogger<RadarEngine> log)
{
    const int MaxBatch = 200;

    /// <param name="fetch">False: only collect results of batches already submitted.</param>
    public async Task<RadarRun> RunAsync(bool fetch, CancellationToken ct)
    {
        var o = options.CurrentValue;
        var now = time.GetUtcNow().UtcDateTime;
        var run = new RadarRun { StartedAt = now, FetchedBoards = fetch };
        var errors = new List<string>();

        if (fetch)
            await FetchAndStoreAsync(o, run, errors, now, ct);
        await SubmitPendingAsync(o, run, errors, ct);
        await CollectAsync(run, errors, now, ct);
        await NotifyAsync(o, errors, ct);

        run.FinishedAt = time.GetUtcNow().UtcDateTime;
        run.Errors = errors.Count == 0 ? null : string.Join("\n", errors);
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.RadarRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }
        log.LogInformation("Radar run: fetched {Fetched}, new {New}, passed filter {Passed}, submitted {Submitted}, scored {Scored}{Errors}",
            run.Fetched, run.New, run.Passed, run.Submitted, run.Scored, errors.Count > 0 ? $", {errors.Count} error(s)" : "");
        return run;
    }

    async Task FetchAndStoreAsync(RadarOptions o, RadarRun run, List<string> errors, DateTime now, CancellationToken ct)
    {
        // Fetch a few companies at a time: fast, but polite to the APIs.
        var fetched = new ConcurrentDictionary<RadarCompany, IReadOnlyList<BoardPosting>>();
        await Parallel.ForEachAsync(o.Companies, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (c, token) =>
        {
            try
            {
                fetched[c] = await boards.FetchAsync(c, token);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                lock (errors) errors.Add($"{c.Name}: {e.Message}");
            }
        });

        var filter = new RadarFilter(o);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var (company, postings) in fetched)
        {
            var key = company.Slug.ToLowerInvariant();
            var board = company.Board.ToLowerInvariant();
            var existing = await db.RadarPostings
                .Where(p => p.Board == board && p.CompanyKey == key)
                .ToDictionaryAsync(p => p.ExternalId, ct);

            var seen = new HashSet<string>();
            foreach (var bp in postings)
            {
                if (string.IsNullOrEmpty(bp.ExternalId) || !seen.Add(bp.ExternalId)) continue;
                run.Fetched++;
                if (existing.TryGetValue(bp.ExternalId, out var known))
                {
                    known.LastSeenAt = now;
                    known.Closed = false;
                    continue;
                }

                run.New++;
                var reject = filter.Reject(bp, now);
                var posting = new RadarPosting
                {
                    Board = board, CompanyKey = key, ExternalId = bp.ExternalId, Company = company.Name,
                    Title = Cap(bp.Title, 300), Location = bp.Location is null ? null : Cap(bp.Location, 300),
                    Remote = bp.Remote, Url = Cap(bp.Url, 2000), PostedAt = bp.PostedAt,
                    FirstSeenAt = now, LastSeenAt = now,
                    Status = reject is null ? RadarStatus.Pending : RadarStatus.Filtered,
                    FilterReason = reject,
                    // Only keep the text of postings Claude will read.
                    Text = reject is null ? PostingTrimmer.Trim(bp.Description) : null,
                };
                if (reject is null) run.Passed++;
                db.RadarPostings.Add(posting);
            }

            // Anything this company no longer lists has been filled or pulled.
            foreach (var gone in existing.Values.Where(p => !seen.Contains(p.ExternalId) && !p.Closed))
                gone.Closed = true;
        }
        await db.SaveChangesAsync(ct);
    }

    async Task SubmitPendingAsync(RadarOptions o, RadarRun run, List<string> errors, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pending = await db.RadarPostings
            .Where(p => p.Status == RadarStatus.Pending && !p.Closed)
            .OrderByDescending(p => p.FirstSeenAt)
            .Take(MaxBatch)
            .ToListAsync(ct);
        if (pending.Count == 0) return;

        if (!scorer.IsConfigured)
        {
            errors.Add($"{pending.Count} posting(s) waiting: no Claude API key configured.");
            return;
        }
        var resume = await db.Resumes.AsNoTracking().FirstOrDefaultAsync(ct);
        if (resume is null || resume.IsEmpty)
        {
            errors.Add($"{pending.Count} posting(s) waiting: add your résumé so they can be scored.");
            return;
        }

        try
        {
            var batchId = await scorer.SubmitAsync(pending, resume, o.CandidateNote, ct);
            foreach (var p in pending)
            {
                p.Status = RadarStatus.Scoring;
                p.BatchId = batchId;
            }
            await db.SaveChangesAsync(ct);
            run.Submitted = pending.Count;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Left as Pending, so the next run tries again.
            errors.Add($"Couldn't submit for scoring: {e.Message}");
        }
    }

    async Task CollectAsync(RadarRun run, List<string> errors, DateTime now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var batchIds = await db.RadarPostings
            .Where(p => p.Status == RadarStatus.Scoring && p.BatchId != null)
            .Select(p => p.BatchId!)
            .Distinct()
            .ToListAsync(ct);

        foreach (var batchId in batchIds)
        {
            IReadOnlyDictionary<int, RadarResult>? results;
            try
            {
                results = await scorer.CollectAsync(batchId, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                errors.Add($"Couldn't check batch {batchId}: {e.Message}");
                continue;
            }
            if (results is null) continue; // still running

            var postings = await db.RadarPostings.Where(p => p.BatchId == batchId && p.Status == RadarStatus.Scoring).ToListAsync(ct);
            foreach (var p in postings)
            {
                if (results.TryGetValue(p.Id, out var r) && r.Score is { } s)
                {
                    p.Status = RadarStatus.Scored;
                    (p.Score, p.Verdict, p.Reasons, p.Level) = (s.Score, s.Verdict, s.Reasons, s.Level);
                    p.ScoredAt = now;
                    run.Scored++;
                }
                else
                {
                    p.Status = RadarStatus.Failed;
                    p.FilterReason = r?.Error ?? "missing from batch results";
                }
            }
            await db.SaveChangesAsync(ct);
        }
    }

    async Task NotifyAsync(RadarOptions o, List<string> errors, CancellationToken ct)
    {
        if (!notifier.IsConfigured) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strong = await db.RadarPostings
            .Where(p => p.Status == RadarStatus.Scored && !p.Notified && p.Score >= o.NotifyScore)
            .OrderByDescending(p => p.Score)
            .ToListAsync(ct);
        if (strong.Count == 0) return;
        try
        {
            await notifier.NotifyAsync(strong, ct);
            foreach (var p in strong) p.Notified = true;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            errors.Add($"Couldn't send notification (will retry): {e.Message}");
        }
    }

    static string Cap(string s, int max) => s.Length <= max ? s : s[..max];
}
