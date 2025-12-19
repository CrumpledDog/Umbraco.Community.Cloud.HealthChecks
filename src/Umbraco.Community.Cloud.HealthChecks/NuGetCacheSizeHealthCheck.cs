using Umbraco.Cms.Core.HealthChecks;

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

        protected override string FolderPath => NuGetCachePath;

        protected override long WarningThresholdMb => 2560; // 500 MB

        protected override long ErrorThresholdMb => 3072; // 1 GB

        protected override string FolderDisplayName => "NuGet cache";

        protected override bool ShouldRunCheck()
        {
            // Only run this check if we're in Azure (C:\home exists)
            return Directory.Exists(@"C:\home");
        }
    }
}
