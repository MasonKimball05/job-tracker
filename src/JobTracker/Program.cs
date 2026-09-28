using Anthropic;
using JobTracker.Components;
using JobTracker.Data;
using JobTracker.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Blazor Server keeps one component alive per browser tab, so use a factory
// and create a short-lived DbContext per operation rather than one per request.
builder.Services.AddDbContextFactory<JobDbContext>(o =>
    o.UseSqlite($"Data Source={AppPaths.DatabaseFile()}"));

// The API key comes from .NET user-secrets (stored in your home folder, never
// in the repo) or the ANTHROPIC_API_KEY environment variable.
builder.Services.AddSingleton<IJobAi>(sp =>
{
    var key = builder.Configuration["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    var client = string.IsNullOrWhiteSpace(key) ? null : new AnthropicClient { ApiKey = key };
    return new ClaudeJobAi(client, sp.GetRequiredService<ILogger<ClaudeJobAi>>());
});

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
