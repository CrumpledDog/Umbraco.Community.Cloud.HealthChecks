using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.Cloud.HealthChecks.Services;
using Umbraco.Community.Cloud.HealthChecks.Stats;

namespace Umbraco.Community.Cloud.HealthChecks
{
    [HealthCheck(
        "8F4C5B3A-9D2E-4A1C-B8F7-3E5D9A1B4C2F",
        "NuGet Cache Size",
        Description = "Reports the size of the NuGet cache folder in Azure Web Apps (C:\\home\\.nuget on Windows, /home/.nuget on Linux)",
        Group = "Umbraco Cloud")]
    public class NuGetCacheSizeHealthCheck : FolderSizeHealthCheckBase
    {
        private readonly IStatsTargetCatalog _catalog;
        private readonly IStatsThresholds _thresholds;
        private readonly bool _localTestMode;

        /// <summary>Kept for compatibility: legacy behaviour (type-name result key, no stats service).</summary>
        public NuGetCacheSizeHealthCheck(
            IHealthCheckResultStore store,
            IOptions<CloudHealthChecksOptions> options)
            : base(store, options)
        {
            _catalog = new StatsTargetCatalog(AppContext.BaseDirectory, options.Value, Environment.MachineName, new HostingEnvironmentInfo(), null, Directory.Exists);
            _thresholds = new StatsThresholds(options, _catalog, new DiskUsageReader());
            _localTestMode = options.Value.LocalTestMode;
        }

        [ActivatorUtilitiesConstructor]
        public NuGetCacheSizeHealthCheck(
            IHealthCheckResultStore store,
            IOptions<CloudHealthChecksOptions> options,
            IStatsService stats,
            IStatsTargetCatalog catalog,
            IStatsThresholds thresholds,
            IStatsServers servers)
            : base(store, options, stats, thresholds, servers, catalog)
        {
            _catalog = catalog;
            _thresholds = thresholds;
            _localTestMode = options.Value.LocalTestMode;
            HostingPlatformDetector.ApplyGroup(this, catalog.Platform);
        }

        protected override string? StatsTargetKey => StatsTargetKeys.NuGetCache;

        public override string FolderPath => _catalog.Find(StatsTargetKeys.NuGetCache)!.Path;

        // Configured values, or the adaptive defaults derived from the home quota
        protected override long WarningThresholdMb => _thresholds.Resolve(StatsTargetKeys.NuGetCache).SizeWarningMb ?? 0;

        protected override long ErrorThresholdMb => _thresholds.Resolve(StatsTargetKeys.NuGetCache).SizeErrorMb ?? 0;

        protected override string FolderDisplayName =>
            _localTestMode ? "NuGet cache (test mode)" : "NuGet cache";

        public override bool ShouldRunCheck() => _catalog.Find(StatsTargetKeys.NuGetCache)!.AppliesToThisHost;

        protected override string? GetCleanupScriptUrl()
        {
            return "https://github.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/tree/develop/v1/scripts#nuget";
        }
    }
}
