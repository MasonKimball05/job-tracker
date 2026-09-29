using System.Text;
using JobTracker.Data;

namespace JobTracker.Services.Radar;

public interface IRadarNotifier
{
    bool IsConfigured { get; }
    Task NotifyAsync(IReadOnlyList<RadarPosting> matches, CancellationToken ct);
}

/// <summary>
/// Pushes strong matches to the ntfy phone app. Uses NTFY_URL from the
/// environment (on the desktop: jobtracker.env), the same topic as homebase.
/// </summary>
public class NtfyRadarNotifier(HttpClient http, string? ntfyUrl, string? radarPageUrl) : IRadarNotifier
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ntfyUrl);

    public async Task NotifyAsync(IReadOnlyList<RadarPosting> matches, CancellationToken ct)
    {
        var (title, body) = Format(matches);
        using var req = new HttpRequestMessage(HttpMethod.Post, ntfyUrl) { Content = new StringContent(body, Encoding.UTF8) };
        // ntfy reads metadata from headers; titles must be ASCII-safe.
        req.Headers.Add("Title", title);
        req.Headers.Add("Tags", "dart");
        req.Headers.Add("Priority", "default");
        if (!string.IsNullOrEmpty(radarPageUrl)) req.Headers.Add("Click", radarPageUrl);

        try
        {
            using var resp = await http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            // The message would include the topic URL, which is a secret.
            throw new HttpRequestException($"ntfy failed ({e.StatusCode?.ToString() ?? e.HttpRequestError.ToString()})");
        }
    }

    public static (string Title, string Body) Format(IReadOnlyList<RadarPosting> matches)
    {
        var title = matches.Count == 1
            ? $"Job Radar: {matches[0].Score}% {Ascii(matches[0].Title)} at {Ascii(matches[0].Company)}"
            : $"Job Radar: {matches.Count} new strong matches";
        var body = string.Join("\n", matches.Take(8).Select(m => $"{m.Score}% · {m.Title} · {m.Company}\n   {m.Verdict}"));
        if (matches.Count > 8) body += $"\n…and {matches.Count - 8} more on the Radar page";
        return (title, body);
    }

    // HTTP header values must be ASCII; replace anything else.
    static string Ascii(string s) => new(s.Select(c => c < 128 ? c : '?').ToArray());
}
