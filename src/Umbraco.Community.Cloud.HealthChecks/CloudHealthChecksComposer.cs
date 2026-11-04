using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Extensions;
using Umbraco.Community.Cloud.HealthChecks.Services;
using Umbraco.Community.Cloud.HealthChecks.Stats;

namespace Umbraco.Community.Cloud.HealthChecks
{
    /// <summary>
    /// Composer for registering Cloud Health Checks configuration
    /// </summary>
    public class CloudHealthChecksComposer : IComposer
    {
        public void Compose(IUmbracoBuilder builder)
        {
            builder.Services.Configure<CloudHealthChecksOptions>(
                builder.Config.GetSection(CloudHealthChecksOptions.SectionName));

            var options = builder.Config
                .GetSection(CloudHealthChecksOptions.SectionName)
                .Get<CloudHealthChecksOptions>() ?? new CloudHealthChecksOptions();

            var logger = builder.BuilderLoggerFactory.CreateLogger(typeof(CloudHealthChecksComposer));

            // Register health check result storage based on configuration
            if (options.BackgroundScan.StorageMode == HealthCheckStorageMode.KeyValue)
            {
                logger.LogInformation("Umbraco.Community.Cloud.HealthChecks: Using KeyValue storage for health check results");
                builder.Services.AddSingleton<IHealthCheckResultStore, KeyValueHealthCheckResultStore>();
            }
            else
            {
                logger.LogInformation("Umbraco.Community.Cloud.HealthChecks: Using DistributedCache storage for health check results");
                builder.Services.AddSingleton<IHealthCheckResultStore, DistributedCacheHealthCheckResultStore>();
            }

            // Stats API: catalog of monitored targets, disk usage, thresholds and the measure/record service
            builder.Services.AddSingleton<IDiskUsageReader, DiskUsageReader>();
            builder.Services.AddSingleton<IStatsTargetCatalog, StatsTargetCatalog>();
            builder.Services.AddSingleton<IStatsThresholds, StatsThresholds>();
            builder.Services.AddSingleton<StatsService>();
            builder.Services.AddSingleton<IStatsService>(sp => sp.GetRequiredService<StatsService>());

            // Recurring job that runs on every server role (Single, SchedulingPublisher and Subscriber),
            // so split front-end web apps and per-instance folders are measured too
            builder.Services.AddSingleton<IRecurringBackgroundJob, StatsRefreshJob>();
        }
    }
}
