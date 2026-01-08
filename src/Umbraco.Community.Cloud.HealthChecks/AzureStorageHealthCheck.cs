using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;

namespace Umbraco.Community.Cloud.HealthChecks
{
    [HealthCheck(
        "2E8F9A3B-5D4C-4F1E-9B7A-6C8D2E4F5A9B",
        "Azure Storage Usage",
        Description = "Reports storage usage for C:\\home, C:\\local, and D:\\local in Azure Web Apps",
        Group = "Umbraco Cloud")]
    public class AzureStorageHealthCheck : HealthCheck
    {
        private const string HomeDirectory = @"C:\home";
        private const string LocalDirectory = @"C:\local";
        private const string LocalTempDirectory = @"D:\local";
        private readonly CloudHealthChecksOptions _options;
        private readonly IHostEnvironment _hostEnvironment;

        public AzureStorageHealthCheck(
            IOptions<CloudHealthChecksOptions> options,
            IHostEnvironment hostEnvironment)
        {
            _options = options.Value;
            _hostEnvironment = hostEnvironment;
        }

        public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
        {
            throw new InvalidOperationException($"{GetType().Name} has no actions");
        }

        public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
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
                return Task.FromResult((IEnumerable<HealthCheckStatus>)results);
            }

            // Determine paths based on mode
            string homeDir, localDir, localTempDir;
            string homeDisplayName, localDisplayName, localTempDisplayName;

            if (_options.LocalTestMode)
            {
                // In test mode, use the application's content root
                homeDir = _hostEnvironment.ContentRootPath;
                localDir = Path.Combine(_hostEnvironment.ContentRootPath, "wwwroot");
                localTempDir = Path.GetTempPath();
                homeDisplayName = "Application root (test mode)";
                localDisplayName = "wwwroot folder (test mode)";
                localTempDisplayName = "System temp folder (test mode)";
            }
            else
            {
                // Production mode - only run if we're in Azure (C:\home exists)
                if (!Directory.Exists(HomeDirectory))
                {
                    results.Add(new HealthCheckStatus(
                        "This check is only applicable in Azure Web Apps environment.")
                    {
                        ResultType = StatusResultType.Info
                    });
                    return Task.FromResult((IEnumerable<HealthCheckStatus>)results);
                }

                homeDir = HomeDirectory;
                localDir = LocalDirectory;
                localTempDir = LocalTempDirectory;
                homeDisplayName = "C:\\home";
                localDisplayName = "C:\\local";
                localTempDisplayName = "D:\\local";
            }

            // Check home directory
            CheckDirectoryUsage(homeDir, homeDisplayName, results, _options.AzureStorage.WarningThresholdPercentage, _options.AzureStorage.ErrorThresholdPercentage);

            // Check local directory if it exists
            if (Directory.Exists(localDir))
            {
                CheckDirectoryUsage(localDir, localDisplayName, results, _options.AzureStorage.WarningThresholdPercentage, _options.AzureStorage.ErrorThresholdPercentage);
            }

            // Check local temp directory if it exists
            if (Directory.Exists(localTempDir))
            {
                CheckDirectoryUsage(localTempDir, localTempDisplayName, results, _options.LocalTemp.WarningThresholdPercentage, _options.LocalTemp.ErrorThresholdPercentage);
            }

            return Task.FromResult((IEnumerable<HealthCheckStatus>)results);
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
                GetDiskFreeSpace(directory, out ulong freeBytes, out ulong totalBytes);
                
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

        private static void GetDiskFreeSpace(string path, out ulong freeBytes, out ulong totalBytes)
        {
            if (!GetDiskFreeSpaceEx(path, out freeBytes, out totalBytes, out ulong diskFreeBytes))
            {
                throw new Win32Exception();
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(string path, out ulong freeBytes, out ulong totalBytes, out ulong diskFreeBytes);
    }
}
