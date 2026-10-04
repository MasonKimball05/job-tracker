using JobTracker.Data;
using JobTracker.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Tests;

public class HopFeedTests : IDisposable
{
    static readonly DateOnly Today = new(2026, 10, 3);

    // An in-memory SQLite database lives as long as its connection stays open.
    readonly SqliteConnection conn = new("Data Source=:memory:");

    public HopFeedTests() => conn.Open();
    public void Dispose() => conn.Dispose();

    JobDbContext NewDb()
    {
        var db = new JobDbContext(new DbContextOptionsBuilder<JobDbContext>().UseSqlite(conn).Options);
        db.Database.Migrate();
        return db;
    }

    static JobApplication App(string company, ApplicationStatus status, DateOnly? followUp = null) =>
        new() { Company = company, Role = "Software Engineer", Status = status, FollowUpOn = followUp };

    static RadarPosting Posting(string company, int? score, RadarStatus status = RadarStatus.Scored, bool closed = false) =>
        new() { Company = company, Title = "New Grad SWE", Url = $"https://jobs.example/{company}", Score = score, Status = status,
                Closed = closed, Board = "gh", CompanyKey = company, ExternalId = company, FirstSeenAt = DateTime.UtcNow };

    [Fact]
    public async Task FollowUps_OpenOnly_WithinAWeek_SoonestFirst()
    {
        await using (var db = NewDb())
        {
            db.Applications.AddRange(
                App("Later", ApplicationStatus.Applied, Today.AddDays(5)),
                App("Overdue", ApplicationStatus.Applied, Today.AddDays(-2)),
                App("TooFar", ApplicationStatus.Applied, Today.AddDays(30)),
                App("Closed", ApplicationStatus.Rejected, Today),
                App("NoDate", ApplicationStatus.Applied));
            await db.SaveChangesAsync();
        }
        await using (var db = NewDb())
        {
            var due = await HopFeed.FollowUpsAsync(db, Today);
            Assert.Equal(["Overdue", "Later"], due.Select(f => f.Company));
            Assert.True(due[0].Due);
            Assert.False(due[1].Due);
        }
    }

    [Fact]
    public async Task Matches_MeetTheBar_AreListed_BestFirst()
    {
        await using (var db = NewDb())
        {
            db.RadarPostings.AddRange(
                Posting("Good", 72),
                Posting("Best", 91),
                Posting("Weak", RadarPosting.MatchScore - 1),
                Posting("Gone", 95, closed: true),
                Posting("Dismissed", 88, RadarStatus.Dismissed));
            await db.SaveChangesAsync();
        }
        await using (var db = NewDb())
        {
            var matches = await HopFeed.MatchesAsync(db);
            Assert.Equal(["Best", "Good"], matches.Select(m => m.Company));
        }
    }

    [Fact]
    public async Task Search_NeedsEveryWord_OpenFirst()
    {
        await using (var db = NewDb())
        {
            db.Applications.AddRange(
                App("Northwind", ApplicationStatus.Rejected),
                App("Northwind Labs", ApplicationStatus.Applied),
                App("Contoso", ApplicationStatus.Applied));
            await db.SaveChangesAsync();
        }
        await using (var db = NewDb())
        {
            var found = await HopFeed.SearchAsync(db, "northwind engineer");
            Assert.Equal(["Northwind Labs", "Northwind"], found.Select(a => a.Company));
            Assert.Equal(3, (await HopFeed.SearchAsync(db, "")).Count);
        }
    }
}
