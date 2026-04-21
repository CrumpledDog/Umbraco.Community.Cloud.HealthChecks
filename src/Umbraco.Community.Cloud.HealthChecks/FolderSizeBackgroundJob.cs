using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Umbraco.Community.Cloud.HealthChecks
{
    /// <summary>
    /// Background job that periodically scans folders and caches their sizes for health checks
    /// </summary>
    public class FolderSizeBackgroundJob : IDistributedBackgroundJob
    {
        private readonly IDistributedCache _distributedCache;
        private readonly CloudHealthChecksOptions _options;
        private readonly ILogger<FolderSizeBackgroundJob> _logger;
        private readonly IEnumerable<FolderSizeHealthCheckBase> _healthChecks;

        private static readonly TimeSpan DefaultPeriod = TimeSpan.FromHours(6);

        public TimeSpan Period => GetPeriod(_options);

        public string Name => "FolderSizeBackgroundJob";

        // No-op event as the period never changes on this job
        public event EventHandler PeriodChanged { add { } remove { } }

        public FolderSizeBackgroundJob(
            IDistributedCache distributedCache,
            IOptions<CloudHealthChecksOptions> options,
            ILogger<FolderSizeBackgroundJob> logger,
            IEnumerable<FolderSizeHealthCheckBase> healthChecks)
        {
            _distributedCache = distributedCache;
            _options = options.Value;
            _logger = logger;
            _healthChecks = healthChecks;
        }

        public async Task ExecuteAsync()
        {
            if (!_options.BackgroundScan.Enabled)
            {
                _logger.LogDebug("Folder size background scanning is disabled");
                return;
            }

            _logger.LogInformation("Starting folder size background scan for {Count} health checks", _healthChecks.Count());

            foreach (var healthCheck in _healthChecks)
            {
                try
                {
                    if (!healthCheck.ShouldRunCheck())
                    {
                        _logger.LogDebug("Skipping {HealthCheck} - check not applicable", healthCheck.GetType().Name);
                        continue;
                    }

                    var folderPath = healthCheck.FolderPath;
                    if (!Directory.Exists(folderPath))
                    {
                        _logger.LogDebug("Skipping {HealthCheck} - folder not found: {Path}", healthCheck.GetType().Name, folderPath);
                        continue;
                    }

                    _logger.LogDebug("Scanning folder for {HealthCheck}: {Path}", healthCheck.GetType().Name, folderPath);

                    var result = ScanFolder(folderPath);

                    var cacheKey = GetCacheKey(healthCheck.GetType().Name);
                    var json = JsonSerializer.Serialize(result);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);

                    await _distributedCache.SetAsync(
                        cacheKey,
                        bytes,
                        new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(_options.BackgroundScan.CacheExpirationHours)
                        });

                    _logger.LogInformation(
                        "Cached folder size for {HealthCheck}: {Size:N0} bytes, {Files:N0} files",
                        healthCheck.GetType().Name,
                        result.SizeInBytes,
                        result.FileCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scanning folder for {HealthCheck}", healthCheck.GetType().Name);

                    // Cache the error result so the health check can report it
                    var errorResult = new FolderSizeResult
                    {
                        CalculatedAt = DateTime.UtcNow,
                        ErrorMessage = ex.Message
                    };

                    var cacheKey = GetCacheKey(healthCheck.GetType().Name);
                    var json = JsonSerializer.Serialize(errorResult);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);

                    await _distributedCache.SetAsync(
                        cacheKey,
                        bytes,
                        new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(_options.BackgroundScan.CacheExpirationHours)
                        });
                }
            }

            _logger.LogInformation("Completed folder size background scan");
        }

        private FolderSizeResult ScanFolder(string path)
        {
            long totalSize = 0;
            int fileCount = 0;
            FileInfo? oldestFile = null;

            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            };

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
                }
                catch (FileNotFoundException)
                {
                    // File was deleted during enumeration
                }
            }

            return new FolderSizeResult
            {
                SizeInBytes = totalSize,
                FileCount = fileCount,
                OldestFileLastWriteTime = oldestFile?.LastWriteTime,
                CalculatedAt = DateTime.UtcNow
            };
        }

        private static TimeSpan GetPeriod(CloudHealthChecksOptions options)
        {
            if (options.BackgroundScan.ScanIntervalHours > 0)
            {
                return TimeSpan.FromHours(options.BackgroundScan.ScanIntervalHours);
            }
            return DefaultPeriod;
        }

        public static string GetCacheKey(string healthCheckName)
        {
            return $"{Constants.ApiName}:foldersize:{healthCheckName}";
        }
    }
}
