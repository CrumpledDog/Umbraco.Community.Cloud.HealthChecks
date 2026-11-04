using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.Cloud.HealthChecks.Services;
using Umbraco.Community.Cloud.HealthChecks.Stats;

namespace Umbraco.Community.Cloud.HealthChecks
{
    [HealthCheck(
        "9A7D4E2B-6F3C-4D8E-A1B2-7C9E5F4A8D3B",
        "Umbraco Logs Folder Size",
        Description = "Reports the size of the umbraco/Logs folder to monitor log file growth",
        Group = "Umbraco Cloud")]
    public class UmbracoLogsFolderSizeHealthCheck : FolderSizeHealthCheckBase
    {
        private readonly IHostEnvironment _hostEnvironment;
        private readonly CloudHealthChecksOptions _options;

        /// <summary>Kept for compatibility: legacy behaviour (type-name result key, no stats service).</summary>
        public UmbracoLogsFolderSizeHealthCheck(
            IHealthCheckResultStore store,
            IHostEnvironment hostEnvironment,
            IOptions<CloudHealthChecksOptions> options)
            : base(store, options)
        {
            _hostEnvironment = hostEnvironment;
            _options = options.Value;
        }

        [ActivatorUtilitiesConstructor]
        public UmbracoLogsFolderSizeHealthCheck(
            IHealthCheckResultStore store,
            IHostEnvironment hostEnvironment,
            IOptions<CloudHealthChecksOptions> options,
            IStatsService stats,
            IStatsThresholds thresholds,
            IStatsTargetCatalog catalog,
            IStatsServers servers)
            : base(store, options, stats, thresholds, servers, catalog)
        {
            _hostEnvironment = hostEnvironment;
            _options = options.Value;
            HostingPlatformDetector.ApplyGroup(this, catalog.Platform);
        }

        protected override string? StatsTargetKey => StatsTargetKeys.UmbracoLogs;

        public override string FolderPath => Path.Combine(_hostEnvironment.ContentRootPath, "umbraco", "Logs");

        protected override long WarningThresholdMb => _options.UmbracoLogs.WarningThresholdMb;

        protected override long ErrorThresholdMb => _options.UmbracoLogs.ErrorThresholdMb;

        protected override string FolderDisplayName => "Umbraco Logs folder";

        protected override int FileAgeWarningThresholdDays => _options.UmbracoLogs.FileAgeWarningThresholdDays;

        protected override int FileAgeErrorThresholdDays => _options.UmbracoLogs.FileAgeErrorThresholdDays;

        protected override string? GetCleanupScriptUrl()
        {
            return "https://github.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/tree/develop/v1/scripts#logs";
        }
    }
}
