using System.Threading.Channels;
using JobTracker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobTracker.Services.Radar;

/// <summary>
/// Runs the radar in the background while the app is up. The desktop is only
/// on during the day, so rather than a fixed schedule it scans when a scan is
/// overdue (e.g. right after the morning boot), then every few hours, and
/// checks on outstanding Claude batches every few minutes in between.
/// </summary>
public class RadarService(
    RadarEngine engine,
    IDbContextFactory<JobDbContext> dbFactory,
    IOptionsMonitor<RadarOptions> options,
    TimeProvider time,
    ILogger<RadarService> log) : BackgroundService
{
    static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    static readonly TimeSpan PollEvery = TimeSpan.FromMinutes(5);

    readonly Channel<bool> scanRequests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public bool Running { get; private set; }

    /// <summary>"Scan now" on the Radar page.</summary>
    public void RequestScan() => scanRequests.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // Let the app (and the network, right after boot) settle first.
        await Task.Delay(StartupDelay, time, stop);

        while (!stop.IsCancellationRequested)
        {
            var o = options.CurrentValue;
            var manual = scanRequests.Reader.TryRead(out _);
            if (o.Enabled || manual)
            {
                var fetch = manual || await ScanIsDueAsync(o, stop);
                var outstanding = await HasOutstandingBatchesAsync(stop);
                if (fetch || outstanding)
                {
                    Running = true;
                    try
                    {
                        await engine.RunAsync(fetch, stop);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        log.LogError(e, "Radar run failed");
                    }
                    finally
                    {
                        Running = false;
                    }
                }
            }

            // Sleep until the next poll, or until someone presses "Scan now".
            using var wake = CancellationTokenSource.CreateLinkedTokenSource(stop);
            wake.CancelAfter(PollEvery);
            try
            {
                await scanRequests.Reader.WaitToReadAsync(wake.Token);
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested)
            {
                // Poll interval elapsed.
            }
        }
    }

    async Task<bool> ScanIsDueAsync(RadarOptions o, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var last = await db.RadarRuns.Where(r => r.FetchedBoards && r.FinishedAt != null)
            .MaxAsync(r => (DateTime?)r.FinishedAt, ct);
        return last is null || time.GetUtcNow().UtcDateTime - last >= TimeSpan.FromHours(o.ScanEveryHours);
    }

    async Task<bool> HasOutstandingBatchesAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Only in-flight batches need polling. Postings waiting on a résumé or
        // API key are retried at the next scan (or "Scan now").
        return await db.RadarPostings.AnyAsync(p => p.Status == RadarStatus.Scoring, ct);
    }
}
