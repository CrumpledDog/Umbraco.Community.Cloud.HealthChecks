using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.Cloud.HealthChecks.Services;
using Umbraco.Community.Cloud.HealthChecks.Stats;

namespace Umbraco.Community.Cloud.HealthChecks
{
    [HealthCheck(
        "4C7D1E9A-2B38-4F6A-8D05-1E9B7A3C5D62",
        "Local Temp Folder Size",
        Description = "Reports the size of each server's local temp folder (per instance) and the usage of the disk it is on",
        Group = "Umbraco Cloud")]
    public class LocalTempFolderSizeHealthCheck : FolderSizeHealthCheckBase
    {
        private readonly IStatsTargetCatalog _catalog;

        public LocalTempFolderSizeHealthCheck(
            IHealthCheckResultStore store,
            IOptions<CloudHealthChecksOptions> options,
            IStatsService stats,
            IStatsThresholds thresholds,
            IStatsServers servers,
            IStatsTargetCatalog catalog)
            : base(store, options, stats, thresholds, servers, catalog)
        {
            _catalog = catalog;
            HostingPlatformDetector.ApplyGroup(this, catalog.Platform);
        }

        protected override string? StatsTargetKey => StatsTargetKeys.LocalTemp;

        public override string FolderPath => _catalog.Find(StatsTargetKeys.LocalTemp)!.Path;

        // No size thresholds: the temp drive's usage percentage is evaluated instead (LocalTemp options)
        protected override long WarningThresholdMb => 0;

        protected override long ErrorThresholdMb => 0;

        protected override string FolderDisplayName => "Local temp folder";

        public override bool ShouldRunCheck() => _catalog.Find(StatsTargetKeys.LocalTemp)!.AppliesToThisHost;
    }
}
