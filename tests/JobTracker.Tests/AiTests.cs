using System.Text.Json;
using JobTracker.Data;
using JobTracker.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobTracker.Tests;

public class AiTests
{
    [Fact]
    public void ParsePosting_TidiesClaudeOutput()
    {
        var json = """
            {"company":"  Acme  ","role":"Junior Developer","location":null,"work_mode":"Hybrid",
             "salary":"   ","requirements":["C#"," SQL ","c#",""],"nice_to_haves":[],"summary":"Builds things."}
            """;

        var p = AiSchemas.ParsePosting(json);

        Assert.Equal("Acme", p.Company);
        Assert.Null(p.Salary); // whitespace-only becomes null
        Assert.Equal(["C#", "SQL"], p.Requirements); // trimmed, blank and case-insensitive duplicate dropped
    }

    [Fact]
    public void ParseMatch_ClampsScore()
    {
        var m = AiSchemas.ParseMatch("""{"score":140,"verdict":"Great","strengths":[],"gaps":[],"suggestions":[]}""");
        Assert.Equal(100, m.Score);
    }

    [Fact]
    public void MalformedJson_BecomesFriendlyError()
    {
        var e = Assert.Throws<AiException>(() => AiSchemas.ParsePosting("{not json"));
        Assert.Contains("expected format", e.Message);
    }

    // Structured outputs reject schemas whose objects aren't closed or that
    // leave keys optional, so check every property is required.
    [Theory]
    [MemberData(nameof(Schemas))]
    public void Schemas_AreClosedAndFullyRequired(string name, Dictionary<string, JsonElement> schema)
    {
        Assert.False(schema["additionalProperties"].GetBoolean(), name);
        var props = schema["properties"].EnumerateObject().Select(p => p.Name).Order();
        var required = schema["required"].EnumerateArray().Select(e => e.GetString()!).Order();
        Assert.Equal(props, required);
    }

    public static TheoryData<string, Dictionary<string, JsonElement>> Schemas() => new()
    {
        { "posting", AiSchemas.Posting },
        { "match", AiSchemas.Match },
    };

    [Fact]
    public void SchemaKeys_MatchRecordProperties()
    {
        // If a record property is renamed without updating the schema, parsing
        // would silently drop it. Round-trip a record through the schema's keys.
        var sample = new PostingInfo("C", "R", "L", "Remote", "S", ["a"], ["b"], "sum");
        var serialized = JsonSerializer.SerializeToElement(sample, AiSchemas.Json);
        var recordKeys = serialized.EnumerateObject().Select(p => p.Name).Order();
        var schemaKeys = AiSchemas.Posting["properties"].EnumerateObject().Select(p => p.Name).Order();
        Assert.Equal(schemaKeys, recordKeys);
    }

    [Fact]
    public async Task WithoutApiKey_EveryFeatureExplainsHowToFix()
    {
        IJobAi ai = new ClaudeJobAi(null, NullLogger<ClaudeJobAi>.Instance);
        Assert.False(ai.IsConfigured);

        var e = await Assert.ThrowsAsync<AiException>(() => ai.ExtractPostingAsync("posting"));
        Assert.Contains("user-secrets", e.Message);

        var resume = new Resume { Text = "me" };
        var job = new JobApplication { Company = "C", Role = "R" };
        await Assert.ThrowsAsync<AiException>(() => ai.AnalyzeMatchAsync(resume, job));
        await Assert.ThrowsAsync<AiException>(async () =>
        {
            await foreach (var _ in ai.DraftCoverLetterAsync(resume, job, null)) { }
        });
    }
}
