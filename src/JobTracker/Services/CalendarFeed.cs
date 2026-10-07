using System.Text;
using JobTracker.Data;

namespace JobTracker.Services;

/// <summary>
/// Follow-up dates as an iCalendar feed (RFC 5545), so Apple Calendar can
/// subscribe to them: GET /calendar.ics. Each open application with a follow-up
/// date becomes an all-day event that links back to its page here.
/// </summary>
public static class CalendarFeed
{
    public static string Build(IEnumerable<JobApplication> applications, string baseUrl, DateTime now)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Mason Kimball//Job Tracker//EN",
            "CALSCALE:GREGORIAN",
            "X-WR-CALNAME:Job follow-ups",
            // Ask subscribers to check back every few hours.
            "REFRESH-INTERVAL;VALUE=DURATION:PT4H",
            "X-PUBLISHED-TTL:PT4H",
        };
        foreach (var app in applications.Where(a => a.IsOpen && a.FollowUpOn is not null).OrderBy(a => a.FollowUpOn))
        {
            var day = app.FollowUpOn!.Value;
            var page = $"{baseUrl.TrimEnd('/')}/applications/{app.Id}";
            var details = $"Status: {app.Status}" + (app.MatchScore is { } s ? $" · {s}% match" : "") + $"\n{page}";
            lines.AddRange([
                "BEGIN:VEVENT",
                $"UID:followup-{app.Id}@jobtracker",
                $"DTSTAMP:{now.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}",
                $"DTSTART;VALUE=DATE:{day:yyyyMMdd}",
                $"DTEND;VALUE=DATE:{day.AddDays(1):yyyyMMdd}",
                $"SUMMARY:{Escape($"Follow up: {app.Role} at {app.Company}")}",
                $"DESCRIPTION:{Escape(details)}",
                $"URL:{page}",
                "TRANSP:TRANSPARENT", // a reminder, not a meeting: don't show as busy
                "END:VEVENT",
            ]);
        }
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines.Select(Fold)) + "\r\n";
    }

    /// <summary>Backslashes, semicolons, commas and newlines are escaped in text values (RFC 5545, 3.3.11).</summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.Replace("\r\n", "\n"))
        {
            sb.Append(c switch
            {
                '\\' => @"\\",
                ';' => @"\;",
                ',' => @"\,",
                '\n' => @"\n",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    /// <summary>Lines over 75 bytes continue on the next line after a space (3.1), never splitting a character.</summary>
    public static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;
        var parts = new List<string>();
        var current = new StringBuilder();
        var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            var limit = parts.Count == 0 ? 75 : 74; // continuation lines start with a space
            if (bytes + size > limit)
            {
                parts.Add(current.ToString());
                current.Clear();
                bytes = 0;
            }
            current.Append(rune.ToString());
            bytes += size;
        }
        parts.Add(current.ToString());
        return string.Join("\r\n ", parts);
    }
}
