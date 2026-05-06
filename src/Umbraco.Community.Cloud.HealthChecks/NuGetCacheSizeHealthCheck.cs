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
        private readonly CloudHealthChecksOptions _options;

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

        protected override long WarningThresholdMb => _options.NuGetCache.WarningThresholdMb;

        protected override long ErrorThresholdMb => _options.NuGetCache.ErrorThresholdMb;

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
    }
}
