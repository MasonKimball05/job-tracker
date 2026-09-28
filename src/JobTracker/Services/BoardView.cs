using JobTracker.Data;

namespace JobTracker.Services;

public enum BoardSort
{
    Recent,
    BestMatch,
    FollowUp,
    Company,
}

/// <summary>What the board is filtered and sorted by. Every field is optional.</summary>
public record BoardFilter(
    string? Search = null,
    string? WorkMode = null,
    string? Location = null,
    int? MinMatch = null,
    BoardSort Sort = BoardSort.Recent)
{
    /// <summary>The value the work-mode filter uses for jobs with no mode set.</summary>
    public const string UnknownMode = "Unknown";

    public bool IsActive =>
        !string.IsNullOrWhiteSpace(Search) || WorkMode is not null || Location is not null || MinMatch is not null;

    public bool Matches(JobApplication a)
    {
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var q = Search.Trim();
            if (!Contains(a.Company, q) && !Contains(a.Role, q) && !Contains(a.Notes, q))
                return false;
        }
        if (WorkMode is not null)
        {
            var mode = string.IsNullOrWhiteSpace(a.WorkMode) ? UnknownMode : a.WorkMode;
            if (!mode.Equals(WorkMode, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (Location is not null && !string.Equals(a.Location?.Trim(), Location, StringComparison.OrdinalIgnoreCase))
            return false;
        // An unscored job never passes a minimum-score filter.
        if (MinMatch is { } min && (a.MatchScore is null || a.MatchScore < min))
            return false;
        return true;
    }

    static bool Contains(string? field, string q) =>
        field?.Contains(q, StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Groups applications for the board. Pure logic, so it's unit-tested.</summary>
public class BoardView
{
    /// <summary>The pipeline columns, in order. Rejected/Withdrawn go in a separate "closed" list.</summary>
    public static readonly ApplicationStatus[] Pipeline =
        [ApplicationStatus.Saved, ApplicationStatus.Applied, ApplicationStatus.Interviewing, ApplicationStatus.Offer];

    public required IReadOnlyDictionary<ApplicationStatus, List<JobApplication>> Columns { get; init; }
    public required List<JobApplication> Closed { get; init; }
    public required List<JobApplication> FollowUpsDue { get; init; }
    /// <summary>Distinct locations across all jobs, for the location filter.</summary>
    public required List<string> Locations { get; init; }

    // Stats cover your whole search, not just the filtered view.
    public int Total { get; init; }
    public int Applied { get; init; }
    public int Interviewing { get; init; }
    public double? ResponseRate { get; init; }

    /// <summary>How many jobs pass the current filter.</summary>
    public int Shown { get; init; }

    public static BoardView Build(IEnumerable<JobApplication> apps, DateOnly today, BoardFilter? filter = null)
    {
        filter ??= new BoardFilter();
        var all = apps.ToList();
        var shown = Sort(all.Where(filter.Matches), filter.Sort, today).ToList();

        // Applied: has an application date, or is at a stage that implies one.
        // Responded: the company got back to you (interview, offer, or a rejection after applying).
        static bool WasApplied(JobApplication a) =>
            a.AppliedOn is not null || a.Status is ApplicationStatus.Applied or ApplicationStatus.Interviewing or ApplicationStatus.Offer;
        var applied = all.Count(WasApplied);
        var responded = all.Count(a => WasApplied(a) &&
            a.Status is ApplicationStatus.Interviewing or ApplicationStatus.Offer or ApplicationStatus.Rejected);

        return new BoardView
        {
            // Filtering and sorting happen once; each column keeps that order.
            Columns = Pipeline.ToDictionary(status => status, status => shown.Where(a => a.Status == status).ToList()),
            Closed = shown.Where(a => !Pipeline.Contains(a.Status)).ToList(),
            // Due follow-ups always show, even when filtered out: they're reminders.
            FollowUpsDue = all.Where(a => a.FollowUpDue(today)).OrderBy(a => a.FollowUpOn).ToList(),
            Locations = all
                .Select(a => a.Location?.Trim())
                .OfType<string>()
                .Where(l => l.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Total = all.Count,
            Applied = applied,
            Interviewing = all.Count(a => a.Status == ApplicationStatus.Interviewing),
            ResponseRate = applied == 0 ? null : (double)responded / applied,
            Shown = shown.Count,
        };
    }

    static IEnumerable<JobApplication> Sort(IEnumerable<JobApplication> apps, BoardSort sort, DateOnly today) => sort switch
    {
        // Unscored jobs sink to the bottom rather than counting as 0.
        BoardSort.BestMatch => apps
            .OrderBy(a => a.MatchScore is null)
            .ThenByDescending(a => a.MatchScore)
            .ThenByDescending(a => a.UpdatedAt),
        BoardSort.FollowUp => apps
            .OrderBy(a => a.FollowUpOn is null)
            .ThenBy(a => a.FollowUpOn)
            .ThenByDescending(a => a.UpdatedAt),
        BoardSort.Company => apps
            .OrderBy(a => a.Company, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Role, StringComparer.OrdinalIgnoreCase),
        // Recent: overdue follow-ups first, then most recently touched.
        _ => apps
            .OrderByDescending(a => a.FollowUpDue(today))
            .ThenByDescending(a => a.UpdatedAt),
    };
}
