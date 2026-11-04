using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Community.Cloud.HealthChecks.Services;

namespace Umbraco.Community.Cloud.HealthChecks.Stats;

/// <summary>
/// Reads, measures and records folder and disk statistics per server. Results are stored through
/// <see cref="IHealthCheckResultStore"/>: shared targets under the site key, per instance targets under the machine id.
/// </summary>
public interface IStatsService
{
    /// <summary>
    /// The latest stored result for a target on the given server (default: this server), or null when none exists.
    /// Never scans. Disk targets on the current server are read live (instant, no storage); for other servers
    /// the disk numbers last stored by that server's background job are returned.
    /// </summary>
    /// <param name="targetKey">A key from <see cref="IStatsTargetCatalog"/>.</param>
    /// <param name="server">Which server's result to read. Shared targets use its <see cref="ServerKey.SiteKey"/>, per instance targets its <see cref="ServerKey.MachineId"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<FolderSizeResult?> GetLatestAsync(string targetKey, ServerKey? server = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Measures a target now on this server, stores the result and returns it. For background callers only: a folder
    /// scan can take minutes, never call this in a request path. Folders are scanned in one pass; the scan stops at
    /// <paramref name="timeLimit"/> and a "took longer than the limit, will retry" error result is stored and returned
    /// instead of a partial number. A target that does not apply here, or a missing folder, returns an error result
    /// that is not stored.
    /// </summary>
    /// <param name="targetKey">A key from <see cref="IStatsTargetCatalog"/>.</param>
    /// <param name="timeLimit">Scan time limit; null or non-positive uses <c>BackgroundScan:ScanTimeLimitMinutes</c>.</param>
    /// <param name="cancellationToken">Cancelling throws <see cref="OperationCanceledException"/>.</param>
    Task<FolderSizeResult> MeasureAsync(string targetKey, TimeSpan? timeLimit = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a result for this server without rescanning, for example the size left after a clean-up.
    /// The result is stamped with the target key and this server's identity, and disk numbers are added when missing.
    /// </summary>
    /// <param name="targetKey">A key from <see cref="IStatsTargetCatalog"/>.</param>
    /// <param name="result">The result to publish (copied, not modified).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task RecordAsync(string targetKey, FolderSizeResult result, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class StatsService : IStatsService
{
    private static readonly TimeSpan DefaultTimeLimit = TimeSpan.FromMinutes(15);

    // Result store names used by 1.3.x: keep reading (and writing) them so older instances in a mixed fleet stay correct.
    private static readonly Dictionary<string, string> LegacyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [StatsTargetKeys.UmbracoLogs] = "UmbracoLogsFolderSizeHealthCheck",
        [StatsTargetKeys.NuGetCache] = "NuGetCacheSizeHealthCheck",
    };

    private readonly IHealthCheckResultStore _store;
    private readonly IStatsTargetCatalog _catalog;
    private readonly IDiskUsageReader _disk;
    private readonly CloudHealthChecksOptions _options;
    private readonly ILogger<StatsService> _logger;

    // One scan at a time per process: a scheduled run and an on-demand caller must not walk the same disk together.
    private readonly SemaphoreSlim _scanGate = new(1, 1);

    // Serialises publishing a result; the per-target version lets a scan notice that a result was recorded while it ran.
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private readonly Dictionary<string, int> _versions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the service.</summary>
    public StatsService(
        IHealthCheckResultStore store,
        IStatsTargetCatalog catalog,
        IDiskUsageReader disk,
        IOptions<CloudHealthChecksOptions> options,
        ILogger<StatsService> logger)
    {
        _store = store;
        _catalog = catalog;
        _disk = disk;
        _options = options.Value;
        _logger = logger;
    }

    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    // Seam for tests; production always uses the single-pass scanner.
    internal Func<string, DateTime, CancellationToken, FolderSizeResult> Scanner { get; set; } = FolderScanner.Scan;

    /// <inheritdoc />
    public async Task<FolderSizeResult?> GetLatestAsync(string targetKey, ServerKey? server = null, CancellationToken cancellationToken = default)
    {
        var target = _catalog.Find(targetKey) ?? throw new ArgumentException($"Unknown stats target '{targetKey}'.", nameof(targetKey));
        cancellationToken.ThrowIfCancellationRequested();
        var requested = server ?? _catalog.CurrentServer;
        var isCurrent = IsCurrent(target, requested);

        if (target.Kind == StatsKind.Disk && isCurrent)
        {
            return target.AppliesToThisHost ? ReadDisk(target) : null;
        }

        var result = await _store.GetAsync(StoreName(target, requested)).WaitAsync(cancellationToken);

        if (result == null && isCurrent && target.Scope == StatsScope.Shared && LegacyNames.TryGetValue(target.Key, out var legacy))
        {
            // Written by 1.3.x (one result per health check type). Newer instances also write it for every site sharing the
            // store, so ignore a result that names a different site; results without site metadata are genuinely old.
            var legacyResult = await _store.GetAsync(legacy).WaitAsync(cancellationToken);
            if (legacyResult != null && (legacyResult.SiteKey == null || legacyResult.SiteKey == requested.SiteKey))
            {
                result = legacyResult;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<FolderSizeResult> MeasureAsync(string targetKey, TimeSpan? timeLimit = null, CancellationToken cancellationToken = default)
    {
        var target = _catalog.Find(targetKey) ?? throw new ArgumentException($"Unknown stats target '{targetKey}'.", nameof(targetKey));

        cancellationToken.ThrowIfCancellationRequested();

        if (!target.AppliesToThisHost)
        {
            return Failed(target, $"{target.DisplayName} is not applicable on this host");
        }

        if (target.Kind == StatsKind.Disk)
        {
            var disk = ReadDisk(target) ?? Failed(target, $"Disk usage for {target.DisplayName} is not available");
            // Persist failures too, so remote readers don't keep seeing this server's last successful reading
            await PersistAsync(target, disk);
            return disk;
        }

        if (!Directory.Exists(target.Path))
        {
            return Failed(target, $"{target.DisplayName} not found at {target.Path}");
        }

        var limit = timeLimit is { } l && l > TimeSpan.Zero ? l : ConfiguredTimeLimit();

        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            var versionAtStart = VersionOf(target);
            var startedAt = UtcNow();

            using var limitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limitCts.CancelAfter(limit);

            FolderSizeResult result;
            try
            {
                var token = limitCts.Token;
                var now = startedAt;
                result = await Task.Run(() => Scanner(target.Path, now, token), CancellationToken.None);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Scan of {Target} took longer than the {Limit} limit and was abandoned", target.Key, limit);
                result = Failed(target, $"Scanning {target.DisplayName} took longer than the limit of {FormatLimit(limit)}, will retry");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error scanning {Target}", target.Key);
                result = Failed(target, ex.Message);
            }

            Stamp(target, result);

            await _publishGate.WaitAsync(CancellationToken.None);
            try
            {
                if (VersionOf(target) != versionAtStart)
                {
                    // A result was recorded (e.g. after a clean-up) while this scan ran: the scan saw the folder before it
                    // and must not undo it.
                    return await _store.GetAsync(StoreName(target, _catalog.CurrentServer)) ?? result;
                }

                // Another server may have recorded a result for this shared disk (e.g. after a clean-up) while this scan ran.
                // The store has no atomic conditional write, so this narrows the race from the scan's duration to the gap
                // between this read and the write below.
                if (target.Scope == StatsScope.Shared)
                {
                    var stored = await _store.GetAsync(StoreName(target, _catalog.CurrentServer));
                    if (stored is { Success: true } && stored.CalculatedAt > startedAt)
                    {
                        return stored;
                    }
                }

                await PersistAsync(target, result);
                return result;
            }
            finally
            {
                _publishGate.Release();
            }
        }
        finally
        {
            _scanGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RecordAsync(string targetKey, FolderSizeResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        var target = _catalog.Find(targetKey) ?? throw new ArgumentException($"Unknown stats target '{targetKey}'.", nameof(targetKey));

        var copy = new FolderSizeResult
        {
            SizeInBytes = result.SizeInBytes,
            FileCount = result.FileCount,
            OldestFileLastWriteTime = result.OldestFileLastWriteTime,
            CalculatedAt = result.CalculatedAt == default ? UtcNow() : result.CalculatedAt,
            ErrorMessage = result.ErrorMessage,
            DiskTotalBytes = result.DiskTotalBytes,
            DiskFreeBytes = result.DiskFreeBytes
        };

        Stamp(target, copy, overwriteDisk: false);

        // Short critical section (never held during a scan): bump the version so a scan that started earlier won't overwrite this
        await _publishGate.WaitAsync(cancellationToken);
        try
        {
            lock (_versions)
            {
                _versions[target.Key] = VersionOf(target) + 1;
            }

            await PersistAsync(target, copy);
        }
        finally
        {
            _publishGate.Release();
        }
    }

    private int VersionOf(StatsTarget target)
    {
        lock (_versions)
        {
            return _versions.TryGetValue(target.Key, out var v) ? v : 0;
        }
    }

    /// <summary>
    /// Best-effort claim of a shared target for this interval. IDistributedCache and KeyValue have no atomic
    /// create-if-absent, so: read the marker, skip if newer than half the interval, otherwise write it BEFORE scanning.
    /// Two servers may occasionally both scan; that is harmless. A recent successful result (such as one recorded by a
    /// clean-up) also counts, so a clean-up is not immediately followed by a rescan.
    /// </summary>
    internal async Task<bool> TryClaimSharedAsync(StatsTarget target, TimeSpan interval)
    {
        var now = UtcNow();
        var window = TimeSpan.FromTicks(interval.Ticks / 2);

        var marker = await _store.GetAsync(ClaimName(target, _catalog.CurrentServer));
        if (marker != null && now - marker.CalculatedAt < window)
        {
            return false;
        }

        var latest = await _store.GetAsync(StoreName(target, _catalog.CurrentServer));
        if (latest is { Success: true } && now - latest.CalculatedAt < window)
        {
            return false;
        }

        await _store.StoreAsync(ClaimName(target, _catalog.CurrentServer), new FolderSizeResult
        {
            CalculatedAt = now,
            TargetKey = target.Key,
            MachineId = _catalog.CurrentServer.MachineId,
            SiteKey = _catalog.CurrentServer.SiteKey
        });

        return true;
    }

    internal static string StoreName(StatsTarget target, ServerKey server) =>
        target.Scope == StatsScope.Shared
            ? $"stats:shared:{server.SiteKey}:{target.Key}"
            : $"stats:instance:{server.MachineId}:{target.Key}";

    internal static string ClaimName(StatsTarget target, ServerKey server) => $"stats-claim:{server.SiteKey}:{target.Key}";

    private static bool IsCurrentSafe(StatsTarget target, ServerKey a, ServerKey b) =>
        target.Scope == StatsScope.Shared
            ? string.Equals(a.SiteKey, b.SiteKey, StringComparison.Ordinal)
            : string.Equals(a.MachineId, b.MachineId, StringComparison.Ordinal);

    private bool IsCurrent(StatsTarget target, ServerKey requested) => IsCurrentSafe(target, requested, _catalog.CurrentServer);

    private async Task PersistAsync(StatsTarget target, FolderSizeResult result)
    {
        await _store.StoreAsync(StoreName(target, _catalog.CurrentServer), result);

        if (target.Scope == StatsScope.Shared && LegacyNames.TryGetValue(target.Key, out var legacy))
        {
            await _store.StoreAsync(legacy, result);
        }
    }

    private FolderSizeResult? ReadDisk(StatsTarget target)
    {
        var usage = _disk.Read(target.Path);
        if (usage == null)
        {
            return null;
        }

        var result = new FolderSizeResult
        {
            CalculatedAt = UtcNow(),
            DiskTotalBytes = usage.Value.TotalBytes,
            DiskFreeBytes = usage.Value.FreeBytes
        };
        Stamp(target, result, overwriteDisk: false);
        return result;
    }

    private void Stamp(StatsTarget target, FolderSizeResult result, bool overwriteDisk = true)
    {
        result.TargetKey = target.Key;
        result.MachineId = _catalog.CurrentServer.MachineId;
        result.SiteKey = _catalog.CurrentServer.SiteKey;

        if (overwriteDisk || result.DiskTotalBytes == null || result.DiskFreeBytes == null)
        {
            var usage = _disk.Read(target.Path);
            if (usage != null)
            {
                result.DiskTotalBytes = usage.Value.TotalBytes;
                result.DiskFreeBytes = usage.Value.FreeBytes;
            }
        }
    }

    private FolderSizeResult Failed(StatsTarget target, string message)
    {
        var result = new FolderSizeResult { CalculatedAt = UtcNow(), ErrorMessage = message };
        result.TargetKey = target.Key;
        result.MachineId = _catalog.CurrentServer.MachineId;
        result.SiteKey = _catalog.CurrentServer.SiteKey;
        return result;
    }

    private TimeSpan ConfiguredTimeLimit() =>
        _options.BackgroundScan.ScanTimeLimitMinutes > 0
            ? TimeSpan.FromMinutes(_options.BackgroundScan.ScanTimeLimitMinutes)
            : DefaultTimeLimit;

    private static string FormatLimit(TimeSpan limit) =>
        limit.TotalMinutes >= 1 ? $"{limit.TotalMinutes:0.#} minutes" : $"{limit.TotalSeconds:0.#} seconds";
}
