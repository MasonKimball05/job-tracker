using Microsoft.EntityFrameworkCore;

namespace JobTracker.Data;

public class JobDbContext(DbContextOptions<JobDbContext> options) : DbContext(options)
{
    public DbSet<JobApplication> Applications => Set<JobApplication>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<RadarPosting> RadarPostings => Set<RadarPosting>();
    public DbSet<RadarRun> RadarRuns => Set<RadarRun>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<JobApplication>(e =>
        {
            // Store the enum as readable text ("Interviewing") instead of an int.
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(a => a.Status);
            e.HasMany(a => a.Contacts)
                .WithOne()
                .HasForeignKey(c => c.JobApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
            // List<string> properties are stored as JSON columns (EF Core 8+).
            e.PrimitiveCollection(a => a.Requirements);
            e.PrimitiveCollection(a => a.NiceToHaves);
            e.PrimitiveCollection(a => a.MatchStrengths);
            e.PrimitiveCollection(a => a.MatchGaps);
            e.PrimitiveCollection(a => a.MatchSuggestions);
        });

        model.Entity<RadarPosting>(e =>
        {
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            // The same posting must never be stored (and scored) twice.
            e.HasIndex(p => new { p.Board, p.CompanyKey, p.ExternalId }).IsUnique();
            e.HasIndex(p => p.Status);
            e.PrimitiveCollection(p => p.Reasons);
        });
    }

    /// <summary>Stamps UpdatedAt on every modified application.</summary>
    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<JobApplication>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(ct);
    }
}

public static class AppPaths
{
    /// <summary>
    /// The database lives in ~/Library/Application Support/JobTracker (or the
    /// platform equivalent), never inside the repo: it holds your résumé and
    /// job search, which shouldn't end up in git.
    /// </summary>
    public static string DatabaseFile()
    {
        // Override for demos/testing, so they never touch your real data.
        if (Environment.GetEnvironmentVariable("JOBTRACKER_DB") is { Length: > 0 } custom)
            return custom;

        var dir = Path.Combine(
            // On macOS, LocalApplicationData is ~/Library/Application Support.
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "JobTracker");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "jobs.db");
    }
}
