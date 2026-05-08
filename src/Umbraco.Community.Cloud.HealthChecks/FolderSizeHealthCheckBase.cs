using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.Cloud.HealthChecks.Services;

namespace Umbraco.Community.Cloud.HealthChecks
{
    /// <summary>
    /// Base class for health checks that monitor folder sizes
    /// </summary>
    public abstract class FolderSizeHealthCheckBase : HealthCheck
    {
        private readonly IHealthCheckResultStore _store;
        private readonly CloudHealthChecksOptions _options;

        protected FolderSizeHealthCheckBase(
            IHealthCheckResultStore store,
            IOptions<CloudHealthChecksOptions> options)
        {
            _store = store;
            _options = options.Value;
        }
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
                    cachedResult = await _store.GetAsync(GetType().Name);
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
                    // Fall back to live scan if no cached result
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

                var message = $"{FolderDisplayName} size: {sizeDisplay} ({fileCount:N0} files) - calculated {ageDisplay}";

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

                // Check size thresholds
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
                    var warningThresholdDisplay = WarningThresholdMb >= 1024
                        ? $"{WarningThresholdMb / 1024.0:F1} GB"
                        : $"{WarningThresholdMb} MB";
                    var errorThresholdDisplay = ErrorThresholdMb >= 1024
                        ? $"{ErrorThresholdMb / 1024.0:F1} GB"
                        : $"{ErrorThresholdMb} MB";
                    message += $" - Within normal limits (warning: {warningThresholdDisplay}, error: {errorThresholdDisplay}).";
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
