using System.ComponentModel.DataAnnotations;

namespace JobTracker.Data;

public enum RadarStatus
{
    /// <summary>Failed the free keyword filter; kept only so it isn't re-checked.</summary>
    Filtered,
    /// <summary>Passed the filter, waiting to be sent to Claude.</summary>
    Pending,
    /// <summary>In a Claude batch that hasn't finished yet.</summary>
    Scoring,
    Scored,
    Saved,
    Dismissed,
    /// <summary>Claude couldn't score it (see FilterReason).</summary>
    Failed,
}

/// <summary>A job posting found on a company's job board.</summary>
public class RadarPosting
{
    /// <summary>Scores at or above this count as a match: the Radar page's Matches tab and Hop's feed.</summary>
    public const int MatchScore = 60;

    public int Id { get; set; }

    // Where it came from. (Board, CompanyKey, ExternalId) is unique.
    [MaxLength(20)] public string Board { get; set; } = "";
    [MaxLength(100)] public string CompanyKey { get; set; } = "";
    [MaxLength(100)] public string ExternalId { get; set; } = "";

    [MaxLength(200)] public string Company { get; set; } = "";
    [MaxLength(300)] public string Title { get; set; } = "";
    [MaxLength(300)] public string? Location { get; set; }
    public bool Remote { get; set; }
    [MaxLength(2000)] public string Url { get; set; } = "";
    public DateTime? PostedAt { get; set; }

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    /// <summary>No longer listed on the company's board.</summary>
    public bool Closed { get; set; }

    /// <summary>Trimmed posting text sent to Claude (only kept for postings that passed the filter).</summary>
    public string? Text { get; set; }

    public RadarStatus Status { get; set; }
    [MaxLength(300)] public string? FilterReason { get; set; }

    public int? Score { get; set; }
    public string? Verdict { get; set; }
    public List<string> Reasons { get; set; } = [];
    /// <summary>"entry", "mid", "senior" or "unclear", as judged from the requirements.</summary>
    [MaxLength(20)] public string? Level { get; set; }
    [MaxLength(100)] public string? BatchId { get; set; }
    public DateTime? ScoredAt { get; set; }
    public bool Notified { get; set; }

    /// <summary>Set when saved to the board.</summary>
    public int? JobApplicationId { get; set; }
}

/// <summary>One scan, for the Radar page's "last scanned" and error display.</summary>
public class RadarRun
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>False for runs that only collected batch results.</summary>
    public bool FetchedBoards { get; set; }
    public int Fetched { get; set; }
    public int New { get; set; }
    public int Passed { get; set; }
    public int Submitted { get; set; }
    public int Scored { get; set; }
    public string? Errors { get; set; }
}
