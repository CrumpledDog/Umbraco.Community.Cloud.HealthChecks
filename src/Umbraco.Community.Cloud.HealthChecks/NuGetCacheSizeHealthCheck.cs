using Microsoft.Extensions.Options;
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
        private readonly CloudHealthChecksOptions _options;

        public NuGetCacheSizeHealthCheck(IOptions<CloudHealthChecksOptions> options)
        {
            _options = options.Value;
        }

        protected override string FolderPath => NuGetCachePath;

        protected override long WarningThresholdMb => _options.NuGetCache.WarningThresholdMb;

        protected override long ErrorThresholdMb => _options.NuGetCache.ErrorThresholdMb;

        protected override string FolderDisplayName => "NuGet cache";

        protected override bool ShouldRunCheck()
        {
            // Only run this check if we're in Azure (C:\home exists)
            return Directory.Exists(@"C:\home");
        }
    }
}
