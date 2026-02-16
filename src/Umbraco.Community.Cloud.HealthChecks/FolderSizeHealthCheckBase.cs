using Umbraco.Cms.Core.HealthChecks;

namespace Umbraco.Community.Cloud.HealthChecks
{
    /// <summary>
    /// Base class for health checks that monitor folder sizes
    /// </summary>
    public abstract class FolderSizeHealthCheckBase : HealthCheck
    {
        /// <summary>
        /// Gets the path to the folder to check
        /// </summary>
        protected abstract string FolderPath { get; }

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
        protected virtual bool ShouldRunCheck()
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

        public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
        {
            var results = new List<HealthCheckStatus>();

            if (!ShouldRunCheck())
            {
                results.Add(new HealthCheckStatus(
                    $"This check is not applicable in the current environment.")
                {
                    ResultType = StatusResultType.Info
                });
                return Task.FromResult((IEnumerable<HealthCheckStatus>)results);
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
                    return Task.FromResult((IEnumerable<HealthCheckStatus>)results);
                }

                // Calculate folder size, file count, and oldest file in a single pass
                var (sizeInBytes, fileCount, oldestFile) = GetDirectoryInfo(folderPath);
                var sizeInMb = sizeInBytes / (1024.0 * 1024.0);
                var sizeInGb = sizeInBytes / (1024.0 * 1024.0 * 1024.0);

                var sizeDisplay = sizeInGb >= 1
                    ? $"{sizeInGb:F2} GB"
                    : $"{sizeInMb:F2} MB";

                var message = $"{FolderDisplayName} size: {sizeDisplay} ({fileCount:N0} files)";

                // Only check file age if thresholds are configured
                double oldestFileAge = 0;
                if (FileAgeWarningThresholdDays > 0 || FileAgeErrorThresholdDays > 0)
                {
                    oldestFileAge = oldestFile != null ? (DateTime.Now - oldestFile.LastWriteTime).TotalDays : 0;

                    if (oldestFile != null)
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
                if (warnings.Any())
                {
                    message += " - " + string.Join(". ", warnings) + ". Consider cleaning up.";
                    
                    var cleanupUrl = GetCleanupScriptUrl();
                    if (!string.IsNullOrEmpty(cleanupUrl))
                    {
                        message += $" Run cleanup script: {cleanupUrl}";
                    }
                }
                else
                {
                    message += " - Within normal limits.";
                }

                results.Add(new HealthCheckStatus(message)
                {
                    ResultType = resultType,
                    Description = $"Path: {folderPath}"
                });
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

            return Task.FromResult((IEnumerable<HealthCheckStatus>)results);
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
