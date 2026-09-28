using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobTracker.Services;

// Shapes Claude returns, plus the JSON schemas that force those shapes.
// Records are C#'s concise immutable data types: the compiler generates the
// constructor, equality, and ToString.

public record PostingInfo(
    string Company,
    string Role,
    string? Location,
    string WorkMode,
    string? Salary,
    List<string> Requirements,
    List<string> NiceToHaves,
    string Summary);

public record MatchAnalysis(
    int Score,
    string Verdict,
    List<string> Strengths,
    List<string> Gaps,
    List<string> Suggestions);

/// <summary>A failure the UI can show as-is.</summary>
public class AiException(string message, Exception? inner = null) : Exception(message, inner);

public static class AiSchemas
{
    // The schemas use snake_case keys; this maps them onto the PascalCase records.
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    static object Str(string description) => new { type = "string", description };
    static object NullableStr(string description) => new { type = new[] { "string", "null" }, description };
    static object StrList(string description) => new { type = "array", items = new { type = "string" }, description };

    /// <summary>Structured outputs need every object closed (additionalProperties: false) and every key required.</summary>
    static Dictionary<string, JsonElement> Object(Dictionary<string, object> properties) => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(properties),
        ["required"] = JsonSerializer.SerializeToElement(properties.Keys.ToArray()),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };

    public static Dictionary<string, JsonElement> Posting { get; } = Object(new()
    {
        ["company"] = Str("Hiring company name. Use \"Unknown\" if not stated."),
        ["role"] = Str("Job title, as written in the posting."),
        ["location"] = NullableStr("City/state/country, or null if not stated."),
        ["work_mode"] = new { type = "string", @enum = new[] { "Onsite", "Hybrid", "Remote", "Unknown" } },
        ["salary"] = NullableStr("Pay range exactly as stated (e.g. \"$70,000–$85,000/yr\"), or null if not stated. Never estimate."),
        ["requirements"] = StrList("Required qualifications, one short phrase each, most important first. At most 12."),
        ["nice_to_haves"] = StrList("Preferred / bonus qualifications, one short phrase each. At most 8."),
        ["summary"] = Str("Two sentences: what the role does and who it is for."),
    });

    public static Dictionary<string, JsonElement> Match { get; } = Object(new()
    {
        ["score"] = new { type = "integer", description = "0-100: how well the résumé meets the stated requirements." },
        ["verdict"] = Str("One sentence overall assessment."),
        ["strengths"] = StrList("Requirements the résumé clearly meets, each tied to specific résumé evidence."),
        ["gaps"] = StrList("Requirements the résumé does not show evidence for."),
        ["suggestions"] = StrList("Concrete, honest ways to present existing experience better for this role. Never suggest claiming experience the résumé doesn't show."),
    });

    /// <summary>Parses and tidies an extraction: trims, drops blanks and duplicates, caps list sizes.</summary>
    public static PostingInfo ParsePosting(string json)
    {
        var p = Deserialize<PostingInfo>(json);
        return p with
        {
            Company = Clean(p.Company) ?? "Unknown",
            Role = Clean(p.Role) ?? "Unknown role",
            Location = Clean(p.Location),
            Salary = Clean(p.Salary),
            WorkMode = Clean(p.WorkMode) ?? "Unknown",
            Requirements = CleanList(p.Requirements, 12),
            NiceToHaves = CleanList(p.NiceToHaves, 8),
            Summary = Clean(p.Summary) ?? "",
        };
    }

    public static MatchAnalysis ParseMatch(string json)
    {
        var m = Deserialize<MatchAnalysis>(json);
        return m with
        {
            Score = Math.Clamp(m.Score, 0, 100),
            Verdict = Clean(m.Verdict) ?? "",
            Strengths = CleanList(m.Strengths, 12),
            Gaps = CleanList(m.Gaps, 12),
            Suggestions = CleanList(m.Suggestions, 8),
        };
    }

    static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json) ?? throw new AiException("Claude returned an empty result.");
        }
        catch (JsonException e)
        {
            throw new AiException("Claude's response wasn't in the expected format. Try again.", e);
        }
    }

    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static List<string> CleanList(List<string>? items, int max) =>
        (items ?? [])
            .Select(Clean)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
}
