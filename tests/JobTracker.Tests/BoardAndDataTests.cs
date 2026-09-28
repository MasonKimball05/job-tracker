using JobTracker.Data;
using JobTracker.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Tests;

public class BoardViewTests
{
    static readonly DateOnly Today = new(2026, 10, 1);

    static JobApplication App(ApplicationStatus s, DateOnly? applied = null, DateOnly? followUp = null) =>
        new() { Company = "C", Role = s.ToString(), Status = s, AppliedOn = applied, FollowUpOn = followUp };

    [Fact]
    public void GroupsPipelineAndSeparatesClosed()
    {
        var view = BoardView.Build(
        [
            App(ApplicationStatus.Saved),
            App(ApplicationStatus.Applied, Today),
            App(ApplicationStatus.Rejected, Today),
            App(ApplicationStatus.Withdrawn),
        ], Today);

        Assert.Single(view.Columns[ApplicationStatus.Saved]);
        Assert.Single(view.Columns[ApplicationStatus.Applied]);
        Assert.Equal(2, view.Closed.Count);
        Assert.Equal(4, view.Total);
    }

    [Fact]
    public void FollowUpsDue_OnlyForOpenApplications()
    {
        var yesterday = Today.AddDays(-1);
        var view = BoardView.Build(
        [
            App(ApplicationStatus.Applied, Today, followUp: yesterday),   // due
            App(ApplicationStatus.Applied, Today, followUp: Today),       // due today
            App(ApplicationStatus.Applied, Today, followUp: Today.AddDays(3)), // not yet
            App(ApplicationStatus.Rejected, Today, followUp: yesterday),  // closed: never due
        ], Today);

        Assert.Equal(2, view.FollowUpsDue.Count);
        // Due items sort to the top of their column.
        Assert.True(view.Columns[ApplicationStatus.Applied][0].FollowUpDue(Today));
    }

    [Fact]
    public void ResponseRate_CountsInterviewsOffersAndRejectionsAfterApplying()
    {
        var view = BoardView.Build(
        [
            App(ApplicationStatus.Applied, Today),        // applied, no response yet
            App(ApplicationStatus.Interviewing, Today),   // response
            App(ApplicationStatus.Rejected, Today),       // response (a "no")
            App(ApplicationStatus.Rejected),              // never applied: ignored
            App(ApplicationStatus.Saved),                 // not applied
        ], Today);

        Assert.Equal(3, view.Applied);
        Assert.Equal(2.0 / 3, view.ResponseRate!.Value, precision: 3);
    }

    [Theory]
    [InlineData(ApplicationStatus.Saved, false)]
    [InlineData(ApplicationStatus.Withdrawn, false)]
    [InlineData(ApplicationStatus.Applied, true)]
    [InlineData(ApplicationStatus.Interviewing, true)]
    public void StampAppliedDate_OnlyOncePastSaved(ApplicationStatus status, bool stamped)
    {
        var app = App(status);
        app.StampAppliedDate(Today);
        Assert.Equal(stamped ? Today : null, app.AppliedOn);
    }

    [Fact]
    public void StampAppliedDate_KeepsAnExistingDate()
    {
        var earlier = Today.AddDays(-10);
        var app = App(ApplicationStatus.Interviewing, applied: earlier);
        app.StampAppliedDate(Today);
        Assert.Equal(earlier, app.AppliedOn);
    }

    [Fact]
    public void ResponseRate_IsNullWithNothingApplied()
    {
        Assert.Null(BoardView.Build([App(ApplicationStatus.Saved)], Today).ResponseRate);
    }
}

public class BoardFilterTests
{
    static readonly DateOnly Today = new(2026, 10, 1);

    static JobApplication Job(string company, string? mode = null, string? loc = null, int? score = null,
        ApplicationStatus status = ApplicationStatus.Saved, DateOnly? followUp = null, int ageDays = 0) =>
        new()
        {
            Company = company, Role = "Engineer", WorkMode = mode, Location = loc, MatchScore = score,
            Status = status, FollowUpOn = followUp, UpdatedAt = DateTime.UtcNow.AddDays(-ageDays),
        };

    static List<string> Names(BoardView v) => v.Columns[ApplicationStatus.Saved].Select(a => a.Company).ToList();

    [Fact]
    public void FiltersByWorkModeIncludingUnspecified()
    {
        var apps = new[] { Job("A", "Remote"), Job("B", "Hybrid"), Job("C"), Job("D", "remote") };

        Assert.Equal(["A", "D"], Names(BoardView.Build(apps, Today, new(WorkMode: "Remote"))).Order());
        Assert.Equal(["C"], Names(BoardView.Build(apps, Today, new(WorkMode: BoardFilter.UnknownMode))));
    }

    [Fact]
    public void FiltersByLocationAndListsDistinctLocations()
    {
        var apps = new[] { Job("A", loc: "Birmingham, AL"), Job("B", loc: " birmingham, al "), Job("C", loc: "Dallas, TX"), Job("D") };
        var view = BoardView.Build(apps, Today, new(Location: "Birmingham, AL"));

        Assert.Equal(["A", "B"], Names(view).Order());
        Assert.Equal(["Birmingham, AL", "Dallas, TX"], view.Locations); // de-duplicated, sorted, blanks dropped
    }

    [Fact]
    public void MinMatchExcludesUnscoredJobs()
    {
        var apps = new[] { Job("A", score: 80), Job("B", score: 60), Job("C") };
        Assert.Equal(["A"], Names(BoardView.Build(apps, Today, new(MinMatch: 75))));
    }

    [Fact]
    public void SearchMatchesCompanyRoleAndNotes()
    {
        var withNote = Job("B");
        withNote.Notes = "referral from Sam";
        var apps = new[] { Job("Acme"), withNote, Job("C") };

        Assert.Equal(["Acme"], Names(BoardView.Build(apps, Today, new(Search: "acm"))));
        Assert.Equal(["B"], Names(BoardView.Build(apps, Today, new(Search: "SAM"))));
    }

    [Fact]
    public void BestMatchSortPutsUnscoredLast()
    {
        var apps = new[] { Job("None"), Job("Low", score: 40), Job("High", score: 90) };
        Assert.Equal(["High", "Low", "None"], Names(BoardView.Build(apps, Today, new(Sort: BoardSort.BestMatch))));
    }

    [Fact]
    public void FollowUpSortIsSoonestFirst()
    {
        var apps = new[] { Job("Later", followUp: Today.AddDays(9)), Job("None"), Job("Soon", followUp: Today.AddDays(1)) };
        Assert.Equal(["Soon", "Later", "None"], Names(BoardView.Build(apps, Today, new(Sort: BoardSort.FollowUp))));
    }

    [Fact]
    public void StatsIgnoreFiltersButShownCountsThem()
    {
        var apps = new[] { Job("A", "Remote"), Job("B", "Hybrid", status: ApplicationStatus.Interviewing) };
        var view = BoardView.Build(apps, Today, new(WorkMode: "Remote"));

        Assert.Equal(2, view.Total);
        Assert.Equal(1, view.Interviewing); // the filtered-out interview still counts
        Assert.Equal(1, view.Shown);
    }

    [Fact]
    public void DueFollowUpsShowEvenWhenFilteredOut()
    {
        var apps = new[] { Job("Hidden", "Onsite", status: ApplicationStatus.Applied, followUp: Today) };
        var view = BoardView.Build(apps, Today, new(WorkMode: "Remote"));

        Assert.Equal(0, view.Shown);
        Assert.Single(view.FollowUpsDue);
    }
}

public class DataTests : IDisposable
{
    // An in-memory SQLite database lives as long as its connection stays open.
    readonly SqliteConnection conn = new("Data Source=:memory:");

    public DataTests() => conn.Open();
    public void Dispose() => conn.Dispose();

    JobDbContext NewDb()
    {
        var db = new JobDbContext(new DbContextOptionsBuilder<JobDbContext>().UseSqlite(conn).Options);
        db.Database.Migrate(); // runs the real migrations, so this also tests them
        return db;
    }

    [Fact]
    public async Task ListsAndEnumRoundTrip()
    {
        await using (var db = NewDb())
        {
            db.Applications.Add(new JobApplication
            {
                Company = "Acme",
                Role = "Dev",
                Status = ApplicationStatus.Interviewing,
                Requirements = ["C#", "SQL"],
                Contacts = [new Contact { Name = "Pat" }],
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb())
        {
            var app = await db.Applications.Include(a => a.Contacts).SingleAsync();
            Assert.Equal(["C#", "SQL"], app.Requirements);
            Assert.Equal(ApplicationStatus.Interviewing, app.Status);
            Assert.Single(app.Contacts);
        }
    }

    [Fact]
    public async Task DeletingAnApplicationDeletesItsContacts()
    {
        await using var db = NewDb();
        db.Applications.Add(new JobApplication { Company = "A", Role = "R", Contacts = [new Contact { Name = "Pat" }] });
        await db.SaveChangesAsync();

        await db.Applications.ExecuteDeleteAsync();

        Assert.Equal(0, await db.Contacts.CountAsync());
    }

    [Fact]
    public async Task SaveStampsUpdatedAt()
    {
        await using var db = NewDb();
        var app = new JobApplication { Company = "A", Role = "R", UpdatedAt = DateTime.UtcNow.AddDays(-5) };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        app.Notes = "changed";
        await db.SaveChangesAsync();

        Assert.True(app.UpdatedAt > DateTime.UtcNow.AddMinutes(-1));
    }
}
