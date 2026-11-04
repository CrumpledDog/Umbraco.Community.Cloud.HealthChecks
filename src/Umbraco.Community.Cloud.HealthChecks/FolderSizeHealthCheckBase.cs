using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.Cloud.HealthChecks.Services;
using Umbraco.Community.Cloud.HealthChecks.Stats;

namespace Umbraco.Community.Cloud.HealthChecks
{
    /// <summary>
    /// Base class for health checks that monitor folder sizes
    /// </summary>
    public abstract class FolderSizeHealthCheckBase : HealthCheck
    {
        private readonly IHealthCheckResultStore _store;
        private readonly CloudHealthChecksOptions _options;
        private readonly IStatsService? _stats;
        private readonly IStatsThresholds? _thresholds;
        private readonly IStatsServers? _servers;
        private readonly IStatsTargetCatalog? _catalog;

        protected FolderSizeHealthCheckBase(
            IHealthCheckResultStore store,
            IOptions<CloudHealthChecksOptions> options)
        {
            _store = store;
            _options = options.Value;
        }

        /// <summary>
        /// Reads results through the stats service (per server, per site) and evaluates them with the shared thresholds.
        /// Requires <see cref="StatsTargetKey"/>.
        /// </summary>
        protected FolderSizeHealthCheckBase(
            IHealthCheckResultStore store,
            IOptions<CloudHealthChecksOptions> options,
            IStatsService stats,
            IStatsThresholds thresholds)
            : this(store, options)
        {
            _stats = stats;
            _thresholds = thresholds;
        }

        /// <summary>
        /// As above, and also reports the results of the other servers (by name) when more than one server is known.
        /// </summary>
        protected FolderSizeHealthCheckBase(
            IHealthCheckResultStore store,
            IOptions<CloudHealthChecksOptions> options,
            IStatsService stats,
            IStatsThresholds thresholds,
            IStatsServers servers,
            IStatsTargetCatalog catalog)
            : this(store, options, stats, thresholds)
        {
            _servers = servers;
            _catalog = catalog;
        }

        /// <summary>
        /// The stats target (see <see cref="StatsTargetKeys"/>) this check reports on. When null the check reads the legacy
        /// per health check type result from the store.
        /// </summary>
        protected virtual string? StatsTargetKey => null;

        private bool UsesStats => _stats != null && _thresholds != null && !string.IsNullOrEmpty(StatsTargetKey);

        private bool ReportsServers => UsesStats && _servers != null && _catalog != null && _options.BackgroundScan.Enabled;

        /// <summary>
        /// Gets the path to the folder to check
        /// </summary>
        public abstract string FolderPath { get; }

        /// <summary>
        /// Gets the warning threshold in MB
        /// </summary>
        protected abstract long WarningThresholdMb { get; }

        /// <summary>
        /// Gets the error threshold in MB
        /// </summary>
        protected abstract long ErrorThresholdMb { get; }

        /// <summary>
        /// Gets the display name for the folder being checked
        /// </summary>
        protected abstract string FolderDisplayName { get; }

        /// <summary>
        /// Gets the warning threshold for file age in days (0 to disable)
        /// </summary>
        protected virtual int FileAgeWarningThresholdDays { get; } = 0;

        /// <summary>
        /// Gets the error threshold for file age in days (0 to disable)
        /// </summary>
        protected virtual int FileAgeErrorThresholdDays { get; } = 0;

        /// <summary>
        /// Determines if this check should only run in specific environments
        /// </summary>
        public virtual bool ShouldRunCheck()
        {
            return true;
        }

        /// <summary>
        /// Gets additional information to display in the health check result
        /// </summary>
        protected virtual string GetAdditionalInfo(string folderPath)
        {
            return string.Empty;
        }

        /// <summary>
        /// Gets the URL to a cleanup script for this health check
        /// </summary>
        protected virtual string? GetCleanupScriptUrl()
        {
            return null;
        }

        public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
        {
            throw new InvalidOperationException($"{GetType().Name} has no actions");
        }

        public override async Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
        {
            if (!ReportsServers || !ShouldRunCheck())
            {
                return await GetCurrentServerStatusAsync(string.Empty);
            }

            IReadOnlyList<StatsServer> servers;
            try
            {
                servers = await _servers!.GetServersAsync();
            }
            catch (Exception)
            {
                return await GetCurrentServerStatusAsync(string.Empty);
            }

            // Name the servers only when there is more than one, so a single server reads as before
            var results = await GetCurrentServerStatusAsync(servers.Count > 1 ? $"[{servers[0].DisplayName}] " : string.Empty);

            if (servers.Count > 1)
            {
                results.AddRange(await GetOtherServerStatusesAsync(servers));
            }

            return results;
        }

        private async Task<List<HealthCheckStatus>> GetOtherServerStatusesAsync(IReadOnlyList<StatsServer> servers)
        {
            var statuses = new List<HealthCheckStatus>();
            var current = servers[0];
            var target = _catalog!.Find(StatsTargetKey!);
            var others = servers.Skip(1).ToList();

            // A shared disk is one measurement per site: report each other site once, naming the site and its servers
            var units = target?.Scope == StatsScope.Shared
                ? others.Where(s => s.Key.SiteKey != current.Key.SiteKey)
                    .GroupBy(s => s.Key.SiteKey)
                    .Select(g => (Label: $"site {g.Key} ({string.Join(", ", g.Select(s => s.DisplayName))})", Server: g.First()))
                : others.Select(s => (Label: s.DisplayName, Server: s));

            foreach (var (label, server) in units)
            {
                try
                {
                    statuses.Add(RenderOtherServer(label, server, await _stats!.GetLatestAsync(StatsTargetKey!, server.Key)));
                }
                catch (Exception ex)
                {
                    statuses.Add(new HealthCheckStatus($"[{label}] Error reading {FolderDisplayName}: {ex.Message}")
                    {
                        ResultType = StatusResultType.Error
                    });
                }
            }

            return statuses;
        }

        private HealthCheckStatus RenderOtherServer(string label, StatsServer server, FolderSizeResult? result)
        {
            if (result == null)
            {
                return new HealthCheckStatus($"[{label}] {FolderDisplayName}: no measurement has been recorded yet")
                {
                    ResultType = StatusResultType.Info,
                    Description = $"Server: {server.DisplayName} (machine {server.Key.MachineId}, site {server.Key.SiteKey})"
                };
            }

            if (!result.Success)
            {
                return new HealthCheckStatus($"[{label}] Error checking {FolderDisplayName}: {result.ErrorMessage} (result from {result.CalculatedAt:u})")
                {
                    ResultType = StatusResultType.Error,
                    Description = $"Server: {server.DisplayName}"
                };
            }

            var evaluation = _thresholds!.Evaluate(StatsTargetKey!, result);
            var gb = result.SizeInBytes / (1024.0 * 1024.0 * 1024.0);
            var size = gb >= 1 ? $"{gb:F2} GB" : $"{result.SizeInBytes / (1024.0 * 1024.0):F2} MB";
            var ago = DateTime.UtcNow - result.CalculatedAt;
            var agoText = ago.TotalMinutes < 1 ? "just now"
                : ago.TotalHours < 1 ? $"{ago.TotalMinutes:F0} minutes ago"
                : ago.TotalDays < 1 ? $"{ago.TotalHours:F0} hours ago"
                : $"{ago.TotalDays:F0} days ago";

            var message = $"[{label}] {FolderDisplayName} size: {size} ({result.FileCount:N0} files) - calculated {agoText}";
            message += evaluation.Reasons.Count > 0
                ? " - " + string.Join(". ", evaluation.Reasons) + "."
                : evaluation.Level == ThresholdLevel.Ok ? " - Within normal limits." : string.Empty;

            if (result.DiskUsedPercent is { } used)
            {
                message += $" Disk {used:F1}% used.";
            }

            var status = new HealthCheckStatus(message)
            {
                ResultType = evaluation.Level switch
                {
                    ThresholdLevel.Error => StatusResultType.Error,
                    ThresholdLevel.Warning => StatusResultType.Warning,
                    _ => StatusResultType.Success
                },
                Description = $"Server: {server.DisplayName} (machine {server.Key.MachineId}, site {server.Key.SiteKey})"
            };

            var cleanupUrl = GetCleanupScriptUrl();
            if (!string.IsNullOrEmpty(cleanupUrl) && evaluation.Level >= ThresholdLevel.Warning)
            {
                status.ReadMoreLink = cleanupUrl;
            }

            return status;
        }

        private async Task<List<HealthCheckStatus>> GetCurrentServerStatusAsync(string serverPrefix)
        {
            var results = new List<HealthCheckStatus>();

            if (!ShouldRunCheck())
            {
                results.Add(new HealthCheckStatus(
                    $"This check is not applicable in the current environment.")
                {
                    ResultType = StatusResultType.Info
                });
                return results;
            }

            try
            {
                var folderPath = FolderPath;

                // Check if folder exists
                if (!Directory.Exists(folderPath))
                {
                    results.Add(new HealthCheckStatus(
                        $"{FolderDisplayName} not found at {folderPath}")
                    {
                        ResultType = StatusResultType.Info
                    });
                    return results;
                }

                // Try to get cached result first if background scanning is enabled
                FolderSizeResult? cachedResult = null;
                if (_options.BackgroundScan.Enabled)
                {
                    cachedResult = UsesStats
                        ? await _stats!.GetLatestAsync(StatsTargetKey!)
                        : await _store.GetAsync(GetType().Name);
                }

                long sizeInBytes;
                int fileCount;
                DateTime? oldestFileLastWriteTime;
                DateTime calculatedAt;
                bool isFromCache = false;

                if (cachedResult != null)
                {
                    // Use cached result
                    if (!cachedResult.Success)
                    {
                        results.Add(new HealthCheckStatus(
                            $"Error checking {FolderDisplayName}: {cachedResult.ErrorMessage} (cached result from {cachedResult.CalculatedAt:u})")
                        {
                            ResultType = StatusResultType.Error
                        });
                        return results;
                    }

                    sizeInBytes = cachedResult.SizeInBytes;
                    fileCount = cachedResult.FileCount;
                    oldestFileLastWriteTime = cachedResult.OldestFileLastWriteTime;
                    calculatedAt = cachedResult.CalculatedAt;
                    isFromCache = true;
                }
                else
                {
                    if (UsesStats && _options.BackgroundScan.Enabled)
                    {
                        // Never walk the folder in a request: the background job (every server role) will measure it shortly
                        var pending = $"{FolderDisplayName}: no measurement has been recorded for this server yet. The background scan runs shortly after start-up and then every {_options.BackgroundScan.ScanIntervalHours:0.##} hours.";
                        var uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
                        if (uptime > TimeSpan.FromMinutes(10))
                        {
                            // The job starts about 2-3 minutes after start-up: if nothing exists by now it is not running here
                            pending += $" This server has been running for {uptime.TotalMinutes:0} minutes, so the scan is overdue: check that this instance holds MainDom (look for 'Could not register with MainDom' in the logs) and that its server role is Single, SchedulingPublisher or Subscriber.";
                        }

                        results.Add(new HealthCheckStatus(pending)
                        {
                            ResultType = uptime > TimeSpan.FromMinutes(10) ? StatusResultType.Warning : StatusResultType.Info
                        });
                        return results;
                    }

                    // Legacy checks, or background scanning disabled: live scan
                    var (size, count, oldestFile) = GetDirectoryInfo(folderPath);
                    sizeInBytes = size;
                    fileCount = count;
                    oldestFileLastWriteTime = oldestFile?.LastWriteTime;
                    calculatedAt = DateTime.UtcNow;

                    // Store the live scan result to help other instances until the next background scan
                    if (_options.BackgroundScan.Enabled)
                    {
                        var liveScanResult = new FolderSizeResult
                        {
                            SizeInBytes = sizeInBytes,
                            FileCount = fileCount,
                            OldestFileLastWriteTime = oldestFileLastWriteTime,
                            CalculatedAt = calculatedAt
                        };

                        await _store.StoreAsync(GetType().Name, liveScanResult);
                    }
                }

                var sizeInMb = sizeInBytes / (1024.0 * 1024.0);
                var sizeInGb = sizeInBytes / (1024.0 * 1024.0 * 1024.0);

                var sizeDisplay = sizeInGb >= 1
                    ? $"{sizeInGb:F2} GB"
                    : $"{sizeInMb:F2} MB";

                var calculatedAgo = DateTime.UtcNow - calculatedAt;
                var ageDisplay = calculatedAgo.TotalMinutes < 1
                    ? "just now"
                    : calculatedAgo.TotalHours < 1
                        ? $"{calculatedAgo.TotalMinutes:F0} minutes ago"
                        : calculatedAgo.TotalDays < 1
                            ? $"{calculatedAgo.TotalHours:F0} hours ago"
                            : $"{calculatedAgo.TotalDays:F0} days ago";

                var message = $"{serverPrefix}{FolderDisplayName} size: {sizeDisplay} ({fileCount:N0} files) - calculated {ageDisplay}";

                // Only check file age if thresholds are configured
                double oldestFileAge = 0;
                if (FileAgeWarningThresholdDays > 0 || FileAgeErrorThresholdDays > 0)
                {
                    oldestFileAge = oldestFileLastWriteTime != null ? (DateTime.Now - oldestFileLastWriteTime.Value).TotalDays : 0;

                    if (oldestFileLastWriteTime != null)
                    {
                        message += $". Oldest file: {oldestFileAge:F0} days old";
                    }
                }

                // Add any additional info from derived class
                var additionalInfo = GetAdditionalInfo(folderPath);
                if (!string.IsNullOrEmpty(additionalInfo))
                {
                    message += additionalInfo;
                }

                // Determine status based on thresholds
                StatusResultType resultType;
                var warnings = new List<string>();

                if (UsesStats)
                {
                    // Shared evaluation (same thresholds the public API exposes). Reuse the cached result (it carries the measured disk quota); only a live scan builds a fresh one
                    var evaluation = _thresholds!.Evaluate(
                        StatsTargetKey!,
                        cachedResult ?? new FolderSizeResult
                        {
                            SizeInBytes = sizeInBytes,
                            FileCount = fileCount,
                            OldestFileLastWriteTime = oldestFileLastWriteTime,
                            CalculatedAt = calculatedAt
                        });

                    resultType = evaluation.Level switch
                    {
                        ThresholdLevel.Error => StatusResultType.Error,
                        ThresholdLevel.Warning => StatusResultType.Warning,
                        _ => StatusResultType.Success
                    };
                    warnings.AddRange(evaluation.Reasons);
                }
                else
                {
                    // Legacy path (no stats target): check size thresholds
                    if (sizeInMb >= ErrorThresholdMb)
                    {
                        resultType = StatusResultType.Error;
                        warnings.Add($"Exceeds error threshold of {ErrorThresholdMb} MB");
                    }
                    else if (sizeInMb >= WarningThresholdMb)
                    {
                        resultType = StatusResultType.Warning;
                        warnings.Add($"Exceeds warning threshold of {WarningThresholdMb} MB");
                    }
                    else
                    {
                        resultType = StatusResultType.Success;
                    }

                    // Check file age thresholds
                    if (FileAgeErrorThresholdDays > 0 && oldestFileAge >= FileAgeErrorThresholdDays)
                    {
                        resultType = StatusResultType.Error;
                        warnings.Add($"Oldest file age ({oldestFileAge:F0} days) exceeds error threshold of {FileAgeErrorThresholdDays} days");
                    }
                    else if (FileAgeWarningThresholdDays > 0 && oldestFileAge >= FileAgeWarningThresholdDays)
                    {
                        if (resultType != StatusResultType.Error)
                        {
                            resultType = StatusResultType.Warning;
                        }
                        warnings.Add($"Oldest file age ({oldestFileAge:F0} days) exceeds warning threshold of {FileAgeWarningThresholdDays} days");
                    }
                }

                // Build final message
                var cleanupUrl = GetCleanupScriptUrl();

                if (warnings.Any())
                {
                    if (!string.IsNullOrEmpty(cleanupUrl))
                    {
                        message += " - " + string.Join(". ", warnings) + ". Follow the 'Read more' link for cleanup instructions.";
                    }
                    else
                    {
                        message += " - " + string.Join(". ", warnings) + ". Consider cleaning up.";
                    }
                }
                else
                {
                    // Show threshold details even when within normal limits
                    // (from the same measured quota the status was evaluated with)
                    var displayed = UsesStats ? _thresholds!.Resolve(StatsTargetKey!, cachedResult?.DiskTotalBytes) : null;
                    if (displayed != null && displayed.SizeWarningMb == null && displayed.SizeErrorMb == null)
                    {
                        // Disk-usage based target (no size thresholds)
                        message += " - Within normal limits" + (cachedResult?.DiskUsedPercent is { } usedPercent ? $" (disk {usedPercent:F1}% used)" : string.Empty) + ".";
                    }
                    else
                    {
                        var warningMb = displayed?.SizeWarningMb ?? WarningThresholdMb;
                        var errorMb = displayed?.SizeErrorMb ?? ErrorThresholdMb;
                        var warningThresholdDisplay = warningMb >= 1024
                            ? $"{warningMb / 1024.0:F1} GB"
                            : $"{warningMb} MB";
                        var errorThresholdDisplay = errorMb >= 1024
                            ? $"{errorMb / 1024.0:F1} GB"
                            : $"{errorMb} MB";
                        message += $" - Within normal limits (warning: {warningThresholdDisplay}, error: {errorThresholdDisplay}).";
                    }
                }

                var description = $"Path: {folderPath}";
                description += $"\nCalculated at: {calculatedAt.ToLocalTime():g}";
                if (isFromCache)
                {
                    description += " (from cache)";
                }
                else
                {
                    description += " (live scan - persisted until next background scan)";
                }

                var status = new HealthCheckStatus(message)
                {
                    ResultType = resultType,
                    Description = description
                };

                // Add cleanup script link as a "Read more" button if available
                if (!string.IsNullOrEmpty(cleanupUrl))
                {
                    status.ReadMoreLink = cleanupUrl;
                }

                results.Add(status);
            }
            catch (UnauthorizedAccessException ex)
            {
                results.Add(new HealthCheckStatus(
                    $"Access denied when checking {FolderDisplayName}: {ex.Message}")
                {
                    ResultType = StatusResultType.Error
                });
            }
            catch (Exception ex)
            {
                results.Add(new HealthCheckStatus(
                    $"Error checking {FolderDisplayName} size: {ex.Message}")
                {
                    ResultType = StatusResultType.Error
                });
            }

            return results;
        }

        /// <summary>
        /// Efficiently gets directory size, file count, and oldest file in a single pass
        /// </summary>
        protected (long size, int count, FileInfo? oldestFile) GetDirectoryInfo(string path)
        {
            long totalSize = 0;
            int fileCount = 0;
            FileInfo? oldestFile = null;

            try
            {
                // Use EnumerationOptions for better performance and control
                var enumerationOptions = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true, // Skip files/folders we can't access
                    ReturnSpecialDirectories = false
                };

                // Single pass through all files
                foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", enumerationOptions))
                {
                    try
                    {
                        totalSize += file.Length;
                        fileCount++;

                        if (oldestFile == null || file.LastWriteTime < oldestFile.LastWriteTime)
                        {
                            oldestFile = file;
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip individual files we can't access
                        continue;
                    }
                    catch (FileNotFoundException)
                    {
                        // File was deleted during enumeration
                        continue;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Can't access the root directory
            }
            catch (DirectoryNotFoundException)
            {
                // Directory doesn't exist
            }

            return (totalSize, fileCount, oldestFile);
        }
    }
}
