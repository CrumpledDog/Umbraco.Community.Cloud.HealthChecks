using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.Cloud.HealthChecks.Stats;

namespace Umbraco.Community.Cloud.HealthChecks
{
    [HealthCheck(
        "2E8F9A3B-5D4C-4F1E-9B7A-6C8D2E4F5A9B",
        "Azure Storage Usage",
        Description = "Reports storage usage for C:\\home and C:\\local in Azure Web Apps",
        Group = "Umbraco Cloud")]
    public class AzureStorageHealthCheck : HealthCheck
    {
        private const string HomeDirectory = @"C:\home";
        private const string LocalDirectory = @"C:\local";
        private const string LocalTempDirectory = @"D:\local";
        private readonly CloudHealthChecksOptions _options;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly IDiskUsageReader _diskUsageReader;
        private readonly IStatsTargetCatalog _catalog;
        private IStatsServers? _servers;
        private IStatsService? _stats;
        private IStatsThresholds? _thresholds;

        /// <summary>Kept for compatibility.</summary>
        public AzureStorageHealthCheck(
            IOptions<CloudHealthChecksOptions> options,
            IHostEnvironment hostEnvironment)
            : this(
                options,
                hostEnvironment,
                new DiskUsageReader(),
                new StatsTargetCatalog(hostEnvironment.ContentRootPath, options.Value, Environment.MachineName, new HostingEnvironmentInfo(), null, Directory.Exists))
        {
        }

        public AzureStorageHealthCheck(
            IOptions<CloudHealthChecksOptions> options,
            IHostEnvironment hostEnvironment,
            IDiskUsageReader diskUsageReader,
            IStatsTargetCatalog catalog)
        {
            _diskUsageReader = diskUsageReader;
            _catalog = catalog;
            HostingPlatformDetector.ApplyGroup(this, catalog.Platform);
            _options = options.Value;
            _hostEnvironment = hostEnvironment;
        }

        /// <summary>Also reports the disks of the other servers (by name) when more than one server is known.</summary>
        [ActivatorUtilitiesConstructor]
        public AzureStorageHealthCheck(
            IOptions<CloudHealthChecksOptions> options,
            IHostEnvironment hostEnvironment,
            IDiskUsageReader diskUsageReader,
            IStatsTargetCatalog catalog,
            IStatsServers servers,
            IStatsService stats,
            IStatsThresholds thresholds)
            : this(options, hostEnvironment, diskUsageReader, catalog)
        {
            _servers = servers;
            _stats = stats;
            _thresholds = thresholds;
        }

        public override async Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
        {
            var results = GetCurrentServerStatuses();

            if (_servers == null || _stats == null || _thresholds == null
                || !_catalog.Find(StatsTargetKeys.HomeDisk)!.AppliesToThisHost)
            {
                return results;
            }

            try
            {
                var servers = await _servers.GetServersAsync();
                if (servers.Count > 1)
                {
                    // Name this server on its own entries too, not only on the other servers'
                    results = StatusNaming.WithServerName(results, servers[0].DisplayName);

                    results.AddRange(await GetOtherServerStatusesAsync(servers));
                }
            }
            catch (Exception ex)
            {
                results.Add(new HealthCheckStatus($"Could not read the other servers' storage usage: {ex.Message}")
                {
                    ResultType = StatusResultType.Info
                });
            }

            return results;
        }

        private async Task<List<HealthCheckStatus>> GetOtherServerStatusesAsync(IReadOnlyList<StatsServer> servers)
        {
            var statuses = new List<HealthCheckStatus>();
            var current = servers[0];
            var others = servers.Skip(1).ToList();

            // C:\home is one disk per site; C:\local and the temp drive are per instance
            var sharedSites = others.Where(s => s.Key.SiteKey != current.Key.SiteKey).GroupBy(s => s.Key.SiteKey)
                .Select(g => (Label: $"site {g.Key} ({string.Join(", ", g.Select(s => s.DisplayName))})", Server: g.First()));
            foreach (var (label, server) in sharedSites)
            {
                await AddDiskStatusAsync(statuses, label, server, StatsTargetKeys.HomeDisk);
            }

            foreach (var server in others)
            {
                await AddDiskStatusAsync(statuses, server.DisplayName, server, StatsTargetKeys.LocalDisk);
                await AddDiskStatusAsync(statuses, server.DisplayName, server, StatsTargetKeys.TempDisk);
            }

            return statuses;
        }

        private async Task AddDiskStatusAsync(List<HealthCheckStatus> statuses, string label, StatsServer server, string targetKey)
        {
            try
            {
                await AddDiskStatusCoreAsync(statuses, label, server, targetKey);
            }
            catch (Exception ex)
            {
                // One unreadable disk must not hide the others
                statuses.Add(new HealthCheckStatus($"[{label}] Error reading {_catalog.Find(targetKey)?.DisplayName ?? targetKey} storage usage: {ex.Message}")
                {
                    ResultType = StatusResultType.Error,
                    Description = $"Server: {server.DisplayName}"
                });
            }
        }

        private async Task AddDiskStatusCoreAsync(List<HealthCheckStatus> statuses, string label, StatsServer server, string targetKey)
        {
            var result = await _stats!.GetLatestAsync(targetKey, server.Key);
            if (result == null)
            {
                return; // the target does not apply to that server, or it has not reported yet
            }

            var name = _catalog.Find(targetKey)?.DisplayName ?? targetKey;
            if (!result.Success)
            {
                statuses.Add(new HealthCheckStatus($"[{label}] Error checking {name} storage usage: {result.ErrorMessage}")
                {
                    ResultType = StatusResultType.Error,
                    Description = $"Server: {server.DisplayName}"
                });
                return;
            }

            if (result.DiskUsedPercent == null)
            {
                // A stored result without quota numbers must not read as an empty, healthy disk
                statuses.Add(new HealthCheckStatus($"[{label}] {name} storage usage is not available for that server")
                {
                    ResultType = StatusResultType.Info,
                    Description = $"Server: {server.DisplayName}"
                });
                return;
            }

            var total = result.DiskTotalBytes.GetValueOrDefault() / (1024.0 * 1024.0);
            var free = result.DiskFreeBytes.GetValueOrDefault() / (1024.0 * 1024.0);
            var evaluation = _thresholds!.Evaluate(targetKey, result);
            var message = $"[{label}] {name} usage: {total:N0} MB total; {free:N0} MB free ({total - free:N0} MB used, {result.DiskUsedPercent:F1}%)";
            message += evaluation.Reasons.Count > 0 ? " - " + string.Join(". ", evaluation.Reasons) + "." : " - Storage usage is within normal limits.";

            statuses.Add(new HealthCheckStatus(message)
            {
                ResultType = evaluation.Level switch
                {
                    ThresholdLevel.Error => StatusResultType.Error,
                    ThresholdLevel.Warning => StatusResultType.Warning,
                    _ => StatusResultType.Success
                },
                Description = $"Server: {server.DisplayName} (machine {server.Key.MachineId}, site {server.Key.SiteKey}); calculated {result.CalculatedAt.ToLocalTime():g}"
            });
        }

        public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
        {
            throw new InvalidOperationException($"{GetType().Name} has no actions");
        }

        private List<HealthCheckStatus> GetCurrentServerStatuses()
        {
            var results = new List<HealthCheckStatus>();

            // Only supported on Windows
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                results.Add(new HealthCheckStatus(
                    "Azure Storage health check is only supported on Windows.")
                {
                    ResultType = StatusResultType.Success
                });
                return results;
            }

            // Determine paths based on mode
            string homeDir, localDir, localTempDir;
            string homeDisplayName, localDisplayName, localTempDisplayName;

            if (_options.LocalTestMode)
            {
                // In test mode, use the application's content root
                homeDir = _hostEnvironment.ContentRootPath;
                localDir = Path.Combine(_hostEnvironment.ContentRootPath, "wwwroot");
                localTempDir = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
                homeDisplayName = "Application root (test mode)";
                localDisplayName = "wwwroot folder (test mode)";
                localTempDisplayName = "System temp folder (test mode)";
            }
            else
            {
                // Production mode - only run if we're in Azure (C:\home exists)
                if (!_catalog.Find(StatsTargetKeys.HomeDisk)!.AppliesToThisHost || !Directory.Exists(HomeDirectory))
                {
                    results.Add(new HealthCheckStatus(
                        "This check is only applicable on Umbraco Cloud or Azure Web Apps.")
                    {
                        ResultType = StatusResultType.Info
                    });
                    return results;
                }

                homeDir = HomeDirectory;
                localDir = LocalDirectory;

                // Get TEMP and strip \Temp suffix if present (e.g., C:\local\Temp -> C:\local)
                var tempPath = Environment.GetEnvironmentVariable("TEMP") ?? LocalTempDirectory;
                localTempDir = tempPath.EndsWith(@"\Temp", StringComparison.OrdinalIgnoreCase)
                    ? Directory.GetParent(tempPath)?.FullName ?? tempPath
                    : tempPath;

                homeDisplayName = "C:\\home";
                localDisplayName = "C:\\local";
                localTempDisplayName = localTempDir;
            }

            // Check home directory
            CheckDirectoryUsage(homeDir, homeDisplayName, results, _options.AzureStorage.WarningThresholdPercentage, _options.AzureStorage.ErrorThresholdPercentage);

            // Check local directory if it exists
            if (Directory.Exists(localDir))
            {
                CheckDirectoryUsage(localDir, localDisplayName, results, _options.AzureStorage.WarningThresholdPercentage, _options.AzureStorage.ErrorThresholdPercentage);
            }

            // Check local temp directory if it exists and has a different root path
            if (Directory.Exists(localTempDir))
            {
                var localRoot = Path.GetPathRoot(Path.GetFullPath(localDir));
                var tempRoot = Path.GetPathRoot(Path.GetFullPath(localTempDir));
                if (!string.Equals(localRoot, tempRoot, StringComparison.OrdinalIgnoreCase))
                {
                    CheckDirectoryUsage(localTempDir, localTempDisplayName, results, _options.LocalTemp.WarningThresholdPercentage, _options.LocalTemp.ErrorThresholdPercentage);
                }
            }

            return results;
        }

        private void CheckDirectoryUsage(string directory, string displayName, List<HealthCheckStatus> results, double warningThreshold, double errorThreshold, string? additionalInfo = null)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    results.Add(new HealthCheckStatus(
                        $"{displayName} directory not found")
                    {
                        ResultType = StatusResultType.Info
                    });
                    return;
                }

                // Get actual disk space using Windows API (same as Kudu)
                var usage = _diskUsageReader.Read(directory)
                    ?? throw new InvalidOperationException("disk usage could not be read");
                var totalBytes = (ulong)usage.TotalBytes;
                var freeBytes = (ulong)usage.FreeBytes;

                var totalMb = totalBytes / (1024.0 * 1024.0);
                var freeMb = freeBytes / (1024.0 * 1024.0);
                var usedMb = totalMb - freeMb;
                var usedPercentage = ((totalMb - freeMb) / totalMb) * 100;

                var message = $"{displayName} usage: {totalMb:N0} MB total; {freeMb:N0} MB free ({usedMb:N0} MB used, {usedPercentage:F1}%)";

                StatusResultType resultType;
                if (usedPercentage >= errorThreshold)
                {
                    resultType = StatusResultType.Error;
                    message += $" - Critical: Usage exceeds {errorThreshold}% threshold.";
                }
                else if (usedPercentage >= warningThreshold)
                {
                    resultType = StatusResultType.Warning;
                    message += $" - Warning: Usage exceeds {warningThreshold}% threshold.";
                }
                else
                {
                    resultType = StatusResultType.Success;
                    message += " - Storage usage is within normal limits.";
                }

                if (!string.IsNullOrWhiteSpace(additionalInfo))
                {
                    message += $" {additionalInfo}";
                }

                results.Add(new HealthCheckStatus(message)
                {
                    ResultType = resultType,
                    Description = $"Path: {directory}"
                });
            }
            catch (Exception ex)
            {
                results.Add(new HealthCheckStatus(
                    $"Error checking {displayName} storage usage: {ex.Message}")
                {
                    ResultType = StatusResultType.Error
                });
            }
        }
    }
}
