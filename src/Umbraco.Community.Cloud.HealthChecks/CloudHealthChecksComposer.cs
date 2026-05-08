using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Extensions;
using Umbraco.Community.Cloud.HealthChecks.Services;

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

            // Register folder size health checks so background job can find them
            builder.Services.AddSingleton<FolderSizeHealthCheckBase, NuGetCacheSizeHealthCheck>();
            builder.Services.AddSingleton<FolderSizeHealthCheckBase, UmbracoLogsFolderSizeHealthCheck>();

            // Register the distributed background job - runs only on elected SchedulingPublisher
            builder.Services.AddSingleton<IDistributedBackgroundJob, FolderSizeBackgroundJob>();
        }
    }
}
