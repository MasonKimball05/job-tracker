using System.Text;
using System.Text.RegularExpressions;

namespace JobTracker.Services.Radar;

/// <summary>
/// The free first pass: title and location keywords. Most postings fail here,
/// so only plausible ones cost a Claude call.
/// </summary>
public class RadarFilter(RadarOptions o)
{
    readonly Regex? titleIn = Combine(o.TitleInclude);
    readonly Regex? titleOut = Combine(o.TitleExclude);
    readonly Regex? locIn = Combine(o.LocationInclude);
    readonly Regex? locOut = Combine(o.LocationExclude);
    readonly Regex? noAgeLimit = Combine(o.NoAgeLimitTitles);

    /// <summary>Returns null if the posting passes, otherwise why it didn't.</summary>
    public string? Reject(BoardPosting p, DateTime nowUtc)
    {
        if (o.MaxPostingAgeDays > 0 && p.PostedAt is { } posted && nowUtc - posted > TimeSpan.FromDays(o.MaxPostingAgeDays)
            && noAgeLimit?.IsMatch(p.Title) != true)
            return $"posted over {o.MaxPostingAgeDays} days ago";
        if (titleIn is not null && !titleIn.IsMatch(p.Title)) return "title doesn't match the roles you want";
        if (titleOut?.Match(p.Title) is { Success: true } m) return $"title excluded (\"{m.Value}\")";

        var loc = (p.Location ?? "") + (p.Remote ? " remote" : "");
        if (locOut?.Match(loc) is { Success: true } lm) return $"location excluded (\"{lm.Value}\")";
        if (locIn is not null && !locIn.IsMatch(loc)) return "location not in your areas";
        return null;
    }

    /// <summary>Joins patterns into one regex: (?:a)|(?:b). Null when the list is empty.</summary>
    static Regex? Combine(List<string> patterns)
    {
        if (patterns.Count == 0) return null;
        var sb = new StringBuilder();
        foreach (var p in patterns)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append("(?:").Append(p).Append(')');
        }
        // A timeout guards against a pathological pattern hanging the scan.
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    }
}

/// <summary>
/// Cuts company boilerplate ("about us", benefits, legal notices) out of a
/// posting before it's sent to Claude, since that's paid input with no signal.
/// </summary>
public static class PostingTrimmer
{
    public const int MaxChars = 6000;

    // A paragraph that starts with or mostly is one of these is boilerplate.
    static readonly Regex Boilerplate = new(
        @"equal (employment )?opportunity|\bEEO\b|affirmative action|reasonable accommodation|" +
        @"e-?verify|background check|privacy (notice|policy)|applicant privacy|" +
        // "About Acme" is boilerplate; "About the role" / "About you" are the good part.
        @"^(about (us|the company|(?!(the|this|you|your|our role)\b)[A-Z][\w&.]*)|who we are|our (mission|story|values|culture)|why join|life at)\b|" +
        @"^(benefits|perks|what we offer|we offer)\b|401\(?k\)?|health, dental|dental(,| and) vision|paid time off|parental leave|" +
        @"^#?LI-|recruit(ment|ing) (agencies|scams?)|fraudulent",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static string Trim(string description)
    {
        var paragraphs = description.Replace("\r", "")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var kept = new StringBuilder();
        var skipping = false;
        foreach (var para in paragraphs)
        {
            // A boilerplate heading ("Benefits") drops the list under it too,
            // until the next short heading-like paragraph that isn't boilerplate.
            var isHeading = para.Length < 60 && !para.Contains('\n');
            if (Boilerplate.IsMatch(para))
            {
                skipping = isHeading;
                continue;
            }
            if (skipping && !isHeading) continue;
            skipping = false;

            if (kept.Length > 0) kept.Append("\n\n");
            kept.Append(para);
        }

        var text = kept.ToString();
        // If trimming removed essentially everything, the heuristics misfired: keep the original.
        if (text.Length < 80 && description.Length > text.Length) text = description.Trim();
        if (text.Length > MaxChars)
            text = text[..MaxChars] + "\n[…posting truncated for length]";
        return text;
    }
}
