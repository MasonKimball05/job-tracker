namespace JobTracker.Services.Radar;

/// <summary>The "Radar" section of appsettings.json.</summary>
public class RadarOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>How often to check the job boards while the app is running.</summary>
    public double ScanEveryHours { get; set; } = 3;
    /// <summary>Push a phone notification for new matches scoring at least this.</summary>
    public int NotifyScore { get; set; } = 75;
    /// <summary>Extra context for scoring, e.g. "Graduating May 2027; looking for new-grad roles".</summary>
    public string CandidateNote { get; set; } = "Looking for entry-level / new-grad roles.";
    /// <summary>Opened when a notification is tapped, e.g. http://arkans-pc1:5206/radar.</summary>
    public string? PageUrl { get; set; }

    /// <summary>Skip postings first published longer ago than this (0 = no limit).
    /// Keeps the first scan from paying to score a company's months-old backlog.</summary>
    public int MaxPostingAgeDays { get; set; } = 45;
    /// <summary>Titles exempt from the age limit: new-grad postings go up months
    /// early and stay open until the class is hired.</summary>
    public List<string> NoAgeLimitTitles { get; set; } = [];

    public List<RadarCompany> Companies { get; set; } = [];

    // The free keyword filter (case-insensitive regular expressions). A title
    // must match an include and no exclude; likewise for the location.
    public List<string> TitleInclude { get; set; } = [];
    public List<string> TitleExclude { get; set; } = [];
    public List<string> LocationInclude { get; set; } = [];
    public List<string> LocationExclude { get; set; } = [];
}

public class RadarCompany
{
    public string Name { get; set; } = "";
    /// <summary>"greenhouse", "lever" or "ashby".</summary>
    public string Board { get; set; } = "";
    /// <summary>The company's ID on that board, e.g. "fleetio".</summary>
    public string Slug { get; set; } = "";
}
