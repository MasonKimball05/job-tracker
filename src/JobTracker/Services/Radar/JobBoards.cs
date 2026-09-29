using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobTracker.Services.Radar;

/// <summary>A posting as a job board returns it, before filtering.</summary>
public record BoardPosting(
    string ExternalId,
    string Title,
    string? Location,
    bool Remote,
    string Url,
    DateTime? PostedAt,
    string Description);

public interface IJobBoards
{
    Task<IReadOnlyList<BoardPosting>> FetchAsync(RadarCompany company, CancellationToken ct);
}

/// <summary>
/// Reads the public job-board APIs of Greenhouse, Lever and Ashby: the same
/// data their careers pages show, published for exactly this kind of use.
/// </summary>
public class JobBoards(HttpClient http) : IJobBoards
{
    public async Task<IReadOnlyList<BoardPosting>> FetchAsync(RadarCompany c, CancellationToken ct)
    {
        var slug = Uri.EscapeDataString(c.Slug);
        return c.Board.ToLowerInvariant() switch
        {
            "greenhouse" => ParseGreenhouse(await GetJson($"https://boards-api.greenhouse.io/v1/boards/{slug}/jobs?content=true", ct)),
            "lever" => ParseLever(await GetJson($"https://api.lever.co/v0/postings/{slug}?mode=json", ct)),
            "ashby" => ParseAshby(await GetJson($"https://api.ashbyhq.com/posting-api/job-board/{slug}", ct)),
            _ => throw new ArgumentException($"Unknown board \"{c.Board}\" for {c.Name}. Use greenhouse, lever or ashby."),
        };
    }

    async Task<JsonElement> GetJson(string url, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("board not found (check the slug)");
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    // ---- Parsers: public and static so tests can feed them sample JSON ----

    public static List<BoardPosting> ParseGreenhouse(JsonElement root) =>
        root.GetProperty("jobs").EnumerateArray().Select(j =>
        {
            var location = Str(j, "location", "name");
            return new BoardPosting(
                ExternalId: j.GetProperty("id").ToString(),
                Title: Str(j, "title") ?? "",
                Location: location,
                Remote: location?.Contains("remote", StringComparison.OrdinalIgnoreCase) == true,
                Url: Str(j, "absolute_url") ?? "",
                PostedAt: Date(Str(j, "first_published") ?? Str(j, "updated_at")),
                // Greenhouse double-encodes: the HTML itself arrives entity-escaped.
                Description: HtmlToText(WebUtility.HtmlDecode(Str(j, "content") ?? "")));
        }).ToList();

    public static List<BoardPosting> ParseLever(JsonElement root) =>
        root.EnumerateArray().Select(j =>
        {
            var location = Str(j, "categories", "location");
            var text = new StringBuilder(Str(j, "descriptionPlain") ?? "");
            if (j.TryGetProperty("lists", out var lists))
            {
                foreach (var list in lists.EnumerateArray())
                    text.Append("\n\n").Append(Str(list, "text")).Append('\n').Append(HtmlToText(Str(list, "content") ?? ""));
            }
            text.Append("\n\n").Append(Str(j, "additionalPlain"));
            long? createdMs = j.TryGetProperty("createdAt", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64() : null;
            return new BoardPosting(
                ExternalId: Str(j, "id") ?? "",
                Title: Str(j, "text") ?? "",
                Location: location,
                Remote: Str(j, "workplaceType") == "remote" || location?.Contains("remote", StringComparison.OrdinalIgnoreCase) == true,
                Url: Str(j, "hostedUrl") ?? "",
                PostedAt: createdMs is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null,
                Description: text.ToString().Trim());
        }).ToList();

    public static List<BoardPosting> ParseAshby(JsonElement root) =>
        root.GetProperty("jobs").EnumerateArray()
            .Where(j => !j.TryGetProperty("isListed", out var l) || l.ValueKind != JsonValueKind.False)
            .Select(j => new BoardPosting(
                ExternalId: Str(j, "id") ?? "",
                Title: Str(j, "title") ?? "",
                Location: Str(j, "location"),
                Remote: j.TryGetProperty("isRemote", out var r) && r.ValueKind == JsonValueKind.True,
                Url: Str(j, "jobUrl") ?? "",
                PostedAt: Date(Str(j, "publishedAt")),
                Description: Str(j, "descriptionPlain") ?? HtmlToText(Str(j, "descriptionHtml") ?? "")))
            .ToList();

    /// <summary>Reads a nested string property, or null if any step is missing.</summary>
    static string? Str(JsonElement e, params string[] path)
    {
        foreach (var name in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out e)) return null;
        }
        return e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }

    static DateTime? Date(string? s) =>
        DateTimeOffset.TryParse(s, out var d) ? d.UtcDateTime : null;

    static readonly Regex BlockTag = new(@"</?(p|div|br|h[1-6]|ul|ol|tr|section)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex ListItem = new(@"<li\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.Compiled);
    static readonly Regex Blanks = new(@"\n\s*\n\s*(\n\s*)+", RegexOptions.Compiled);

    /// <summary>Good-enough HTML to plain text: keeps paragraphs and bullets, drops markup.</summary>
    public static string HtmlToText(string html)
    {
        var s = BlockTag.Replace(html, "\n");
        s = ListItem.Replace(s, "\n- ");
        s = AnyTag.Replace(s, "");
        s = WebUtility.HtmlDecode(s).Replace(' ', ' ');
        s = string.Join('\n', s.Split('\n').Select(l => l.Trim()));
        return Blanks.Replace(s, "\n\n").Trim();
    }
}
