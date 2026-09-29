using Anthropic;
using JobTracker.Components;
using JobTracker.Data;
using JobTracker.Services;
using JobTracker.Services.Radar;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Blazor Server keeps one component alive per browser tab, so use a factory
// and create a short-lived DbContext per operation rather than one per request.
builder.Services.AddDbContextFactory<JobDbContext>(o =>
    o.UseSqlite($"Data Source={AppPaths.DatabaseFile()}"));

// The API key comes from .NET user-secrets (stored in your home folder, never
// in the repo) or the ANTHROPIC_API_KEY environment variable. One client is
// shared by every Claude feature.
var apiKey = builder.Configuration["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
AnthropicClient? claude = string.IsNullOrWhiteSpace(apiKey) ? null : new AnthropicClient { ApiKey = apiKey };
builder.Services.AddSingleton<IJobAi>(sp => new ClaudeJobAi(claude, sp.GetRequiredService<ILogger<ClaudeJobAi>>()));

// Job Radar: watches company job boards and scores new postings in the background.
builder.Services.Configure<RadarOptions>(builder.Configuration.GetSection("Radar"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IJobBoards, JobBoards>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("JobTracker-Radar/1.0 (personal job search)");
});
builder.Services.AddSingleton<IRadarScorer>(new ClaudeRadarScorer(claude));
builder.Services.AddHttpClient("ntfy", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<IRadarNotifier>(sp => new NtfyRadarNotifier(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("ntfy"),
    Environment.GetEnvironmentVariable("NTFY_URL"),
    builder.Configuration["Radar:PageUrl"]));
builder.Services.AddSingleton<RadarEngine>();
builder.Services.AddSingleton<RadarService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RadarService>());

var app = builder.Build();

// Create or upgrade the database schema on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JobDbContext>>().CreateDbContext();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
