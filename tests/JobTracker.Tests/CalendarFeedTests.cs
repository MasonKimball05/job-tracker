using System.Text;
using JobTracker.Data;
using JobTracker.Services;

namespace JobTracker.Tests;

public class CalendarFeedTests
{
    static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    static JobApplication App(int id, ApplicationStatus status, DateOnly? followUp, string company = "Acme") =>
        new() { Id = id, Company = company, Role = "Software Engineer", Status = status, FollowUpOn = followUp, MatchScore = 80 };

    [Fact]
    public void OpenFollowUps_BecomeAllDayEvents()
    {
        var ics = CalendarFeed.Build([App(7, ApplicationStatus.Applied, new DateOnly(2026, 10, 9))], "http://desktop:5206/", Now);

        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        Assert.Contains("UID:followup-7@jobtracker", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20261009", ics);
        Assert.Contains("DTEND;VALUE=DATE:20261010", ics);
        Assert.Contains("SUMMARY:Follow up: Software Engineer at Acme", ics);
        Assert.Contains("URL:http://desktop:5206/applications/7", ics);
    }

    [Fact]
    public void ClosedOrUndated_AreLeftOut()
    {
        var ics = CalendarFeed.Build(
        [
            App(1, ApplicationStatus.Rejected, new DateOnly(2026, 10, 9), "Gone"),
            App(2, ApplicationStatus.Applied, null, "NoDate"),
        ], "http://desktop:5206", Now);

        Assert.DoesNotContain("BEGIN:VEVENT", ics);
    }

    [Fact]
    public void EscapesTextAndFoldsLongLines()
    {
        Assert.Equal(@"a\;b\,c\\d\ne", CalendarFeed.Escape("a;b,c\\d\ne"));

        var app = App(3, ApplicationStatus.Interviewing, new DateOnly(2026, 10, 9), company: new string('é', 60));
        var ics = CalendarFeed.Build([app], "http://desktop:5206", Now);
        foreach (var line in ics.Split("\r\n"))
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"line too long: {line}");
        Assert.Contains("\r\n ", ics); // the long summary was folded
        // Unfolding gives back the original summary, with no broken characters.
        Assert.Contains($"Follow up: Software Engineer at {new string('é', 60)}", ics.Replace("\r\n ", ""));
    }
}
