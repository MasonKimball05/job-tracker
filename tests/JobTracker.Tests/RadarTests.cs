using System.Text.Json;
using JobTracker.Data;
using JobTracker.Services.Radar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobTracker.Tests;

public class JobBoardParserTests
{
    static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Greenhouse_DecodesDoubleEscapedHtml()
    {
        var jobs = JobBoards.ParseGreenhouse(Json("""
            {"jobs":[{"id":123,"title":"Associate Software Engineer","location":{"name":"Remote"},
              "absolute_url":"https://job-boards.greenhouse.io/x/jobs/123","first_published":"2026-07-15T12:54:32-04:00",
              "content":"&lt;p&gt;You will build &amp;amp; ship.&lt;/p&gt;&lt;ul&gt;&lt;li&gt;C#&lt;/li&gt;&lt;li&gt;SQL&lt;/li&gt;&lt;/ul&gt;"}]}
            """));
        var j = Assert.Single(jobs);
        Assert.Equal("123", j.ExternalId);
        Assert.True(j.Remote);
        Assert.Equal(new DateTime(2026, 7, 15, 16, 54, 32, DateTimeKind.Utc), j.PostedAt);
        Assert.Equal("You will build & ship.\n\n- C#\n- SQL", j.Description);
    }

    [Fact]
    public void Lever_CombinesDescriptionAndLists()
    {
        var jobs = JobBoards.ParseLever(Json("""
            [{"id":"abc","text":"Backend Engineer","categories":{"location":"Dallas, TX"},"workplaceType":"hybrid",
              "hostedUrl":"https://jobs.lever.co/x/abc","createdAt":1782214185805,"descriptionPlain":"Intro.",
              "lists":[{"text":"Who You Are","content":"<li>2 years of Go</li>"}],"additionalPlain":"Benefits."}]
            """));
        var j = Assert.Single(jobs);
        Assert.False(j.Remote);
        Assert.Contains("Who You Are\n- 2 years of Go", j.Description);
        Assert.NotNull(j.PostedAt);
    }

    [Fact]
    public void Ashby_SkipsUnlistedJobs()
    {
        var jobs = JobBoards.ParseAshby(Json("""
            {"jobs":[
              {"id":"1","title":"Product Engineer","location":"Remote: United States","isRemote":true,"jobUrl":"u1","publishedAt":"2026-02-04T01:45:22.580+00:00","descriptionPlain":"Build."},
              {"id":"2","title":"Hidden","isListed":false,"jobUrl":"u2","descriptionPlain":"x"}]}
            """));
        Assert.Equal("Product Engineer", Assert.Single(jobs).Title);
    }
}

public class RadarFilterTests
{
    // The real settings from appsettings.json, so the tests cover what ships.
    static readonly RadarOptions Real = LoadRealOptions();

    static RadarOptions LoadRealOptions()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "JobTracker.slnx"))) dir = Path.GetDirectoryName(dir)!;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "src", "JobTracker", "appsettings.json")));
        return doc.RootElement.GetProperty("Radar").Deserialize<RadarOptions>()!;
    }

    static BoardPosting P(string title, string? location, bool remote = false) =>
        new("1", title, location, remote, "u", null, "");

    [Theory]
    // Wanted: entry-level SWE/security in Birmingham, Dallas, or remote US.
    [InlineData("Associate Software Engineer, Marketplace", "Remote", false, true)]
    [InlineData("Software Engineer", "Birmingham, AL", false, true)]
    [InlineData("Backend Engineer, Observability", "Remote - US", false, true)]
    [InlineData("Associate SOC Analyst", "Remote", false, true)]
    [InlineData("Security Engineer, Cloud", "Dallas, TX", false, true)]
    [InlineData("Product Engineer", null, true, true)] // Ashby-style: remote flag, no location text
    // Not wanted.
    [InlineData("Senior Software Engineer", "Remote", false, false)]
    [InlineData("Staff Security Engineer", "Remote", false, false)]
    [InlineData("Software Engineer II", "Remote", false, false)]
    [InlineData("Software Engineer II - Full Stack", "Remote - USA", false, false)]
    [InlineData("Software Engineer L3", "Remote - US", false, false)]
    [InlineData("Technical Support Engineer", "Remote - Estonia", false, false)]
    [InlineData("Software Engineer, New Grad (2027)", "Vienna, Austria", true, false)]
    [InlineData("Developer Relations Engineer", "Remote, US", false, false)]
    [InlineData("Design Engineer", "Remote - United States", false, false)]
    [InlineData("Software Engineer, New Grad (Dec 2026)", "San Francisco, California", true, true)]
    [InlineData("Software Engineer (L2), Identity", "Remote - US", false, true)]
    [InlineData("SOC Support Specialist", "United States of America", false, false)]
    [InlineData("Engineering Manager", "Remote", false, false)]
    [InlineData("Solutions Engineer", "Remote", false, false)]
    [InlineData("Account Executive", "Remote", false, false)]
    [InlineData("Software Engineer", "London", false, false)]
    [InlineData("Software Engineer", "Remote - Canada", false, false)]
    [InlineData("Software Engineer", "New York, NY", false, false)]
    [InlineData("Software Engineer", "San Francisco, CA", false, false)]
    public void RealFilterSettings(string title, string? location, bool remote, bool passes)
    {
        var reason = new RadarFilter(Real).Reject(P(title, location, remote), DateTime.UtcNow);
        Assert.True((reason is null) == passes, $"{title} @ {location}: {reason ?? "passed"}");
    }

    [Fact]
    public void OldPostingsAreSkipped()
    {
        var old = new BoardPosting("1", "Software Engineer", "Remote", true, "u", DateTime.UtcNow.AddDays(-90), "");
        var fresh = old with { PostedAt = DateTime.UtcNow.AddDays(-3) };
        var undated = old with { PostedAt = null };
        var f = new RadarFilter(Real);
        Assert.Contains("days ago", f.Reject(old, DateTime.UtcNow));
        Assert.Null(f.Reject(fresh, DateTime.UtcNow));
        Assert.Null(f.Reject(undated, DateTime.UtcNow)); // unknown date: give it a chance
        var oldNewGrad = old with { Title = "Software Engineer, New Grad (Dec 2026)" };
        Assert.Null(f.Reject(oldNewGrad, DateTime.UtcNow)); // new-grad roles stay open for months
    }

    [Fact]
    public void RealSettingsListTheCompanies()
    {
        Assert.True(Real.Companies.Count >= 20);
        Assert.All(Real.Companies, c => Assert.Contains(c.Board, new[] { "greenhouse", "lever", "ashby" }));
    }
}

public class PostingTrimmerTests
{
    [Fact]
    public void DropsBoilerplateKeepsRequirements()
    {
        var posting = string.Join("\n\n",
            "About Fleetio",
            "Fleetio is a modern platform for fleet management, founded in 2012 and loved by thousands of customers across the world.",
            "What you'll do",
            "- Build features in Ruby and React\n- Write tests",
            "What we're looking for",
            "- 0-2 years of experience\n- Familiarity with SQL",
            "Benefits",
            "- Health, dental and vision\n- 401(k) match\n- Unlimited PTO",
            "Fleetio is an equal opportunity employer and does not discriminate on the basis of race, religion or anything else.");

        var trimmed = PostingTrimmer.Trim(posting);

        Assert.Contains("0-2 years of experience", trimmed);
        Assert.Contains("Build features in Ruby and React", trimmed);
        Assert.DoesNotContain("founded in 2012", trimmed);
        Assert.DoesNotContain("401(k)", trimmed);
        Assert.DoesNotContain("equal opportunity", trimmed);
    }

    [Fact]
    public void KeepsAboutTheRoleAndAboutYou()
    {
        var posting = "About Acme\n\nAcme makes widgets for everyone and has been doing it for a very long time now.\n\n" +
                      "About the role\n\n- Build APIs in Go for our billing platform\n- Own features from design to deploy\n\n" +
                      "About you\n\n- 1+ year of Python or Go\n- Comfortable with SQL and HTTP APIs";
        var trimmed = PostingTrimmer.Trim(posting);
        Assert.Contains("Build APIs in Go", trimmed);
        Assert.Contains("1+ year of Python or Go", trimmed);
        Assert.DoesNotContain("widgets", trimmed);
    }

    [Fact]
    public void KeepsOriginalIfTrimmingWouldLeaveAlmostNothing()
    {
        var weird = "Equal opportunity is core to us: you will build secure systems with Go and SQL, write tests, and join on-call rotations. " +
                    "We want people with a year of experience and curiosity about security and distributed systems.";
        Assert.Equal(weird, PostingTrimmer.Trim(weird));
    }

    [Fact]
    public void CapsVeryLongPostings()
    {
        var huge = string.Join("\n\n", Enumerable.Range(0, 400).Select(i => $"Requirement number {i}: knows things."));
        var trimmed = PostingTrimmer.Trim(huge);
        Assert.True(trimmed.Length <= PostingTrimmer.MaxChars + 60);
        Assert.EndsWith("[…posting truncated for length]", trimmed);
    }
}

public class RadarEngineTests : IDisposable
{
    readonly SqliteConnection conn = new("Data Source=:memory:");
    readonly FakeBoards boards = new();
    readonly FakeScorer scorer = new();
    readonly FakeNotifier notifier = new();
    readonly RadarOptions opts = new()
    {
        MaxPostingAgeDays = 0,
        NotifyScore = 75,
        Companies = [new RadarCompany { Name = "Acme", Board = "greenhouse", Slug = "acme" }],
        TitleInclude = [@"\bengineer\b"],
        TitleExclude = [@"\bsenior\b"],
        LocationInclude = ["remote"],
    };

    public RadarEngineTests()
    {
        conn.Open();
        using var db = Db();
        db.Database.Migrate();
        db.Resumes.Add(new Resume { Text = "Alex, CS student, Go and SQL." });
        db.SaveChanges();
    }

    public void Dispose() => conn.Dispose();

    JobDbContext Db() => new(new DbContextOptionsBuilder<JobDbContext>().UseSqlite(conn).Options);

    RadarEngine Engine() => new(new Factory(this), boards, scorer, notifier,
        new StaticOptions(opts), TimeProvider.System, NullLogger<RadarEngine>.Instance);

    static BoardPosting Job(string id, string title, string loc = "Remote") =>
        new(id, title, loc, loc.Contains("Remote"), $"https://x/{id}", null, "Build things with Go.\n\nRequirements: SQL.");

    [Fact]
    public async Task FullCycle_FilterSubmitCollectNotify()
    {
        boards.Jobs = [Job("1", "Software Engineer"), Job("2", "Senior Software Engineer"), Job("3", "Account Executive")];
        var run = await Engine().RunAsync(fetch: true, default);

        Assert.Equal((3, 3, 1, 1), (run.Fetched, run.New, run.Passed, run.Submitted));
        Assert.Single(scorer.Submitted); // only the one that passed the free filter costs a Claude call

        using (var db = Db())
        {
            var filtered = db.RadarPostings.Where(p => p.Status == RadarStatus.Filtered).ToList();
            Assert.Equal(2, filtered.Count);
            Assert.All(filtered, p => Assert.Null(p.Text)); // don't store text we never use
        }

        // Batch still running: nothing changes.
        run = await Engine().RunAsync(fetch: false, default);
        Assert.Equal(0, run.Scored);

        scorer.Finish(score: 88);
        run = await Engine().RunAsync(fetch: false, default);
        Assert.Equal(1, run.Scored);
        using (var db = Db())
        {
            var p = db.RadarPostings.Single(x => x.ExternalId == "1");
            Assert.Equal((RadarStatus.Scored, 88, true), (p.Status, p.Score, p.Notified));
        }
        Assert.Single(notifier.Sent);

        // Next run: no duplicate notification.
        await Engine().RunAsync(fetch: false, default);
        Assert.Single(notifier.Sent);
    }

    [Fact]
    public async Task SeenPostingsAreNeverReprocessed()
    {
        boards.Jobs = [Job("1", "Software Engineer")];
        await Engine().RunAsync(true, default);
        var run = await Engine().RunAsync(true, default);

        Assert.Equal((1, 0, 0), (run.Fetched, run.New, run.Submitted));
        using var db = Db();
        Assert.Equal(1, db.RadarPostings.Count());
    }

    [Fact]
    public async Task PostingsThatDisappearAreMarkedClosed()
    {
        boards.Jobs = [Job("1", "Software Engineer"), Job("2", "Platform Engineer")];
        await Engine().RunAsync(true, default);
        boards.Jobs = [Job("2", "Platform Engineer")];
        await Engine().RunAsync(true, default);

        using var db = Db();
        Assert.True(db.RadarPostings.Single(p => p.ExternalId == "1").Closed);
        Assert.False(db.RadarPostings.Single(p => p.ExternalId == "2").Closed);
    }

    [Fact]
    public async Task LowScoresDontNotify()
    {
        boards.Jobs = [Job("1", "Software Engineer")];
        await Engine().RunAsync(true, default);
        scorer.Finish(score: 40);
        await Engine().RunAsync(false, default);
        Assert.Empty(notifier.Sent);
    }

    [Fact]
    public async Task WithoutResumeItWaitsInsteadOfFailing()
    {
        using (var db = Db())
        {
            db.Resumes.RemoveRange(db.Resumes);
            db.SaveChanges();
        }
        boards.Jobs = [Job("1", "Software Engineer")];
        var run = await Engine().RunAsync(true, default);

        Assert.Equal(0, run.Submitted);
        Assert.Contains("add your résumé", run.Errors);
        using var check = Db();
        Assert.Equal(RadarStatus.Pending, check.RadarPostings.Single().Status);
    }

    [Fact]
    public async Task OneBrokenBoardDoesntStopTheRest()
    {
        opts.Companies.Add(new RadarCompany { Name = "Broken", Board = "lever", Slug = "gone" });
        boards.Fail = "gone";
        boards.Jobs = [Job("1", "Software Engineer")];
        var run = await Engine().RunAsync(true, default);

        Assert.Equal(1, run.New);
        Assert.Contains("Broken:", run.Errors);
    }

    [Fact]
    public async Task ScorerErrorsMarkPostingsFailed()
    {
        boards.Jobs = [Job("1", "Software Engineer")];
        await Engine().RunAsync(true, default);
        scorer.Finish(score: null);
        await Engine().RunAsync(false, default);

        using var db = Db();
        var p = db.RadarPostings.Single();
        Assert.Equal(RadarStatus.Failed, p.Status);
        Assert.Contains("refusal", p.FilterReason);
    }

    [Fact]
    public void NotificationFormat()
    {
        var (title, body) = NtfyRadarNotifier.Format(
        [
            new RadarPosting { Title = "Software Engineer", Company = "Fleetio", Score = 91, Verdict = "Strong fit." },
            new RadarPosting { Title = "SOC Analyst", Company = "Expel", Score = 80, Verdict = "Good fit." },
        ]);
        Assert.Equal("Job Radar: 2 new strong matches", title);
        Assert.StartsWith("91% · Software Engineer · Fleetio", body);
    }

    // ---- fakes ----

    sealed class Factory(RadarEngineTests t) : IDbContextFactory<JobDbContext>
    {
        public JobDbContext CreateDbContext() => t.Db();
    }

    sealed class StaticOptions(RadarOptions o) : IOptionsMonitor<RadarOptions>
    {
        public RadarOptions CurrentValue => o;
        public RadarOptions Get(string? name) => o;
        public IDisposable? OnChange(Action<RadarOptions, string?> listener) => null;
    }

    sealed class FakeBoards : IJobBoards
    {
        public List<BoardPosting> Jobs = [];
        public string? Fail;
        public Task<IReadOnlyList<BoardPosting>> FetchAsync(RadarCompany c, CancellationToken ct) =>
            c.Slug == Fail
                ? throw new InvalidOperationException("board not found (check the slug)")
                : Task.FromResult<IReadOnlyList<BoardPosting>>(Jobs);
    }

    sealed class FakeScorer : IRadarScorer
    {
        public List<int> Submitted = [];
        Dictionary<int, RadarResult>? results;
        public bool IsConfigured => true;

        public Task<string> SubmitAsync(IReadOnlyList<RadarPosting> postings, Resume resume, string note, CancellationToken ct)
        {
            Submitted.AddRange(postings.Select(p => p.Id));
            return Task.FromResult("batch-1");
        }

        public void Finish(int? score) => results = Submitted.ToDictionary(id => id, _ => score is { } s
            ? new RadarResult(new RadarScore(s, "Verdict.", ["Reason"], "entry"), null)
            : new RadarResult(null, "not scored (refusal)"));

        public Task<IReadOnlyDictionary<int, RadarResult>?> CollectAsync(string batchId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<int, RadarResult>?>(results);
    }

    sealed class FakeNotifier : IRadarNotifier
    {
        public List<List<string>> Sent = [];
        public bool IsConfigured => true;
        public Task NotifyAsync(IReadOnlyList<RadarPosting> matches, CancellationToken ct)
        {
            Sent.Add(matches.Select(m => m.Title).ToList());
            return Task.CompletedTask;
        }
    }
}
