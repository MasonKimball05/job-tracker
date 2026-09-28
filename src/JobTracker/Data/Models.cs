using System.ComponentModel.DataAnnotations;

namespace JobTracker.Data;

// Entity Framework maps each of these classes to a table. Like Django models,
// but the schema changes are generated as C# "migrations" (see Data/Migrations).

public enum ApplicationStatus
{
    Saved,
    Applied,
    Interviewing,
    Offer,
    Rejected,
    Withdrawn,
}

public class JobApplication
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Company { get; set; } = "";

    [Required, MaxLength(200)]
    public string Role { get; set; } = "";

    [MaxLength(200)]
    public string? Location { get; set; }

    /// <summary>Onsite / Hybrid / Remote, as free text.</summary>
    [MaxLength(50)]
    public string? WorkMode { get; set; }

    [MaxLength(200)]
    public string? Salary { get; set; }

    [MaxLength(2000), Url(ErrorMessage = "Enter a full link starting with https://")]
    public string? PostingUrl { get; set; }

    /// <summary>The original posting, kept so the AI features can re-read it later.</summary>
    public string? PostingText { get; set; }

    public List<string> Requirements { get; set; } = [];
    public List<string> NiceToHaves { get; set; } = [];

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Saved;
    public DateOnly? AppliedOn { get; set; }
    public DateOnly? FollowUpOn { get; set; }
    public string? Notes { get; set; }

    // Latest AI match analysis, if run.
    public int? MatchScore { get; set; }
    public string? MatchSummary { get; set; }
    public List<string> MatchStrengths { get; set; } = [];
    public List<string> MatchGaps { get; set; } = [];

    public string? CoverLetter { get; set; }

    public List<Contact> Contacts { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True when a follow-up date has arrived and the application is still open.</summary>
    public bool FollowUpDue(DateOnly today) =>
        FollowUpOn is { } due && due <= today && IsOpen;

    /// <summary>Fills in AppliedOn when the job has moved past Saved without a date.</summary>
    public void StampAppliedDate(DateOnly today)
    {
        if (AppliedOn is null && Status is not (ApplicationStatus.Saved or ApplicationStatus.Withdrawn))
            AppliedOn = today;
    }

    public bool IsOpen => Status is ApplicationStatus.Saved or ApplicationStatus.Applied or ApplicationStatus.Interviewing;
}

public class Contact
{
    public int Id { get; set; }
    public int JobApplicationId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(320), EmailAddress(ErrorMessage = "That email address doesn't look right.")]
    public string? Email { get; set; }

    public string? Notes { get; set; }
}

/// <summary>The single résumé the AI features compare against: pasted text or a PDF.</summary>
public class Resume
{
    public int Id { get; set; }
    public string? Text { get; set; }
    public byte[]? Pdf { get; set; }
    [MaxLength(260)]
    public string? PdfFileName { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && (Pdf is null || Pdf.Length == 0);
}
