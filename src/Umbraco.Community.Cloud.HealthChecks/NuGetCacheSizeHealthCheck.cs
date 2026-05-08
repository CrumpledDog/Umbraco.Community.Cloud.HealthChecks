using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.Cloud.HealthChecks.Services;

namespace Umbraco.Community.Cloud.HealthChecks
{
    [HealthCheck(
        "8F4C5B3A-9D2E-4A1C-B8F7-3E5D9A1B4C2F",
        "NuGet Cache Size",
        Description = "Reports the size of the NuGet cache folder in Azure Web Apps (C:\\home\\.nuget)",
        Group = "Umbraco Cloud")]
    public class NuGetCacheSizeHealthCheck : FolderSizeHealthCheckBase
    {
        private const string NuGetCachePath = @"C:\home\.nuget";
        private const string HomeDirectory = @"C:\home";
        private readonly CloudHealthChecksOptions _options;
        private long? _calculatedWarningThreshold;
        private long? _calculatedErrorThreshold;

        public NuGetCacheSizeHealthCheck(
            IHealthCheckResultStore store,
            IOptions<CloudHealthChecksOptions> options)
            : base(store, options)
        {
            _options = options.Value;
        }

        public override string FolderPath =>
            _options.LocalTestMode
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
                : NuGetCachePath;

        protected override long WarningThresholdMb => _options.NuGetCache.WarningThresholdMb ?? GetCalculatedWarningThreshold();

        protected override long ErrorThresholdMb => _options.NuGetCache.ErrorThresholdMb ?? GetCalculatedErrorThreshold();

        protected override string FolderDisplayName =>
            _options.LocalTestMode ? "NuGet cache (test mode)" : "NuGet cache";

        public override bool ShouldRunCheck()
        {
            // In test mode, always run. Otherwise only run if we're in Azure (C:\home exists)
            return _options.LocalTestMode || Directory.Exists(@"C:\home");
        }

        protected override string? GetCleanupScriptUrl()
        {
            return "https://github.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/tree/develop/v1/scripts#nuget";
        }

        private long GetCalculatedErrorThreshold()
        {
            if (_calculatedErrorThreshold.HasValue)
            {
                return _calculatedErrorThreshold.Value;
            }

            try
            {
                // Get total home directory size
                var homeDir = _options.LocalTestMode
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : HomeDirectory;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Directory.Exists(homeDir))
                {
                    GetDiskFreeSpace(homeDir, out _, out ulong totalBytes);
                    var totalMb = totalBytes / (1024.0 * 1024.0);

                    // Calculate 50% of total size
                    var fiftyPercentMb = totalMb * 0.5;

                    // Round to nearest GB (1024 MB)
                    var roundedToNearestGb = Math.Round(fiftyPercentMb / 1024.0) * 1024.0;

                    _calculatedErrorThreshold = (long)roundedToNearestGb;
                    return _calculatedErrorThreshold.Value;
                }
            }
            catch
            {
                // If calculation fails, use a safe default
            }

            // Fallback default: 3 GB
            _calculatedErrorThreshold = 3072;
            return _calculatedErrorThreshold.Value;
        }

        private long GetCalculatedWarningThreshold()
        {
            if (_calculatedWarningThreshold.HasValue)
            {
                return _calculatedWarningThreshold.Value;
            }

            // Calculate 75% of error threshold
            var errorThreshold = ErrorThresholdMb;
            var seventyFivePercentOfError = errorThreshold * 0.75;

            // Round to nearest GB (1024 MB)
            var roundedToNearestGb = Math.Round(seventyFivePercentOfError / 1024.0) * 1024.0;

            _calculatedWarningThreshold = (long)roundedToNearestGb;
            return _calculatedWarningThreshold.Value;
        }

        private static void GetDiskFreeSpace(string path, out ulong freeBytes, out ulong totalBytes)
        {
            if (!GetDiskFreeSpaceEx(path, out freeBytes, out totalBytes, out _))
            {
                throw new Win32Exception();
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(string path, out ulong freeBytes, out ulong totalBytes, out ulong diskFreeBytes);
    }
}
