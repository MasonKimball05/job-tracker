using JobTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Services;

/// <summary>
/// Read-only JSON for Hop, my launcher (github.com/MasonKimball05/hop): follow-ups
/// coming due, Radar matches, and a search of the board. It's the same data the
/// pages show, to the same audience (Job Tracker is only reachable over Tailscale),
/// and nothing here changes anything.
/// </summary>
public static class HopFeed
{
    public record FollowUp(int Id, string Company, string Role, string Status, DateOnly FollowUpOn, bool Due);
    public record Match(int Id, string Company, string Title, string? Location, bool Remote, int Score, string? Verdict, string Url);
    public record Application(int Id, string Company, string Role, string Status, int? MatchScore, DateOnly? FollowUpOn);

    static readonly ApplicationStatus[] Open = [ApplicationStatus.Saved, ApplicationStatus.Applied, ApplicationStatus.Interviewing];

    /// <summary>Open applications with a follow-up due by <paramref name="today"/> plus <paramref name="days"/>, soonest first.</summary>
    public static async Task<List<FollowUp>> FollowUpsAsync(JobDbContext db, DateOnly today, int days = 7)
    {
        var until = today.AddDays(days);
        var withDates = await db.Applications.AsNoTracking()
            .Where(a => a.FollowUpOn != null && Open.Contains(a.Status))
            .ToListAsync();
        // Compared here rather than in SQL: SQLite stores DateOnly as text.
        return withDates
            .Where(a => a.FollowUpOn <= until)
            .OrderBy(a => a.FollowUpOn)
            .Select(a => new FollowUp(a.Id, a.Company, a.Role, a.Status.ToString(), a.FollowUpOn!.Value, a.FollowUpDue(today)))
            .ToList();
    }

    /// <summary>Scored postings at or above the match bar that are still listed, best first.</summary>
    public static async Task<List<Match>> MatchesAsync(JobDbContext db, int limit = 30)
    {
        var scored = await db.RadarPostings.AsNoTracking()
            .Where(p => p.Status == RadarStatus.Scored && p.Score >= RadarPosting.MatchScore && !p.Closed)
            .ToListAsync();
        return scored
            .OrderByDescending(p => p.Score).ThenByDescending(p => p.FirstSeenAt)
            .Take(limit)
            .Select(p => new Match(p.Id, p.Company, p.Title, p.Location, p.Remote, p.Score!.Value, p.Verdict, p.Url))
            .ToList();
    }

    /// <summary>Applications whose company or role contain every word of <paramref name="query"/>; open ones first.</summary>
    public static async Task<List<Application>> SearchAsync(JobDbContext db, string? query, int limit = 30)
    {
        var words = (query ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var all = await db.Applications.AsNoTracking().ToListAsync();
        return all
            .Where(a => words.All(w => (a.Company + " " + a.Role).ToLowerInvariant().Contains(w)))
            .OrderByDescending(a => a.IsOpen).ThenByDescending(a => a.UpdatedAt)
            .Take(limit)
            .Select(a => new Application(a.Id, a.Company, a.Role, a.Status.ToString(), a.MatchScore, a.FollowUpOn))
            .ToList();
    }
}
