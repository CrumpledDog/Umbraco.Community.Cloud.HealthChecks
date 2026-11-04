using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Umbraco.Community.Cloud.HealthChecks.Stats;

/// <summary>
/// Refreshes folder and disk statistics. Runs on <em>every</em> server role (like Core's <c>ServerInfoReportingJob</c>):
/// with a split backoffice/front end the front-end instances are Subscribers, which <c>IDistributedBackgroundJob</c>
/// never runs on, so their own disks would never be measured.
/// Shared folders are measured once per site per interval (best-effort claim); per instance folders and disk usage on each server.
/// </summary>
internal sealed class StatsRefreshJob : IRecurringBackgroundJob
{
    private static readonly TimeSpan DefaultPeriod = TimeSpan.FromHours(6);

    private readonly StatsService _stats;
    private readonly StatsServers _servers;
    private readonly IStatsTargetCatalog _catalog;
    private readonly CloudHealthChecksOptions _options;
    private readonly ILogger<StatsRefreshJob> _logger;

    // Spreads instances out so they don't all start at the same second (and read each other's claim marker).
    private readonly TimeSpan _jitter = TimeSpan.FromSeconds(Random.Shared.Next(0, 60));

    public StatsRefreshJob(
        StatsService stats,
        StatsServers servers,
        IStatsTargetCatalog catalog,
        IOptions<CloudHealthChecksOptions> options,
        ILogger<StatsRefreshJob> logger)
    {
        _stats = stats;
        _servers = servers;
        _catalog = catalog;
        _options = options.Value;
        _logger = logger;
    }

    public TimeSpan Period => _options.BackgroundScan.ScanIntervalHours > 0
        ? TimeSpan.FromHours(_options.BackgroundScan.ScanIntervalHours)
        : DefaultPeriod;

    public TimeSpan Delay => TimeSpan.FromMinutes(2) + _jitter;

    public ServerRole[] ServerRoles => new[] { ServerRole.Single, ServerRole.SchedulingPublisher, ServerRole.Subscriber };

    // The period never changes on this job
    public event EventHandler PeriodChanged { add { } remove { } }

    public async Task RunJobAsync()
    {
        try
        {
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Never throw out of a background job
            _logger.LogError(ex, "Umbraco.Community.Cloud.HealthChecks: stats refresh failed");
        }
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!_options.BackgroundScan.Enabled)
        {
            _logger.LogDebug("Background scanning is disabled");
            return;
        }

        // Tell the other servers who this one is (its site key locates its shared results)
        try
        {
            await _servers.PublishCurrentAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not publish this server's identity");
        }

        foreach (var target in _catalog.Targets.Where(t => t.AppliesToThisHost))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Disk usage is a cheap quota read: no claim. Per instance folders are this server's alone.
                var needsClaim = target.Kind == StatsKind.Folder && target.Scope == StatsScope.Shared;
                if (needsClaim && !await _stats.TryClaimSharedAsync(target, Period))
                {
                    _logger.LogDebug("Skipping {Target}: another server measured it recently", target.Key);
                    continue;
                }

                var result = await _stats.MeasureAsync(target.Key, null, cancellationToken);

                if (result.Success)
                {
                    _logger.LogInformation(
                        "Measured {Target}: {Size:N0} bytes, {Files:N0} files",
                        target.Key, result.SizeInBytes, result.FileCount);
                }
                else
                {
                    _logger.LogWarning("Measuring {Target} failed: {Error}", target.Key, result.ErrorMessage);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error measuring {Target}", target.Key);
            }
        }
    }
}
