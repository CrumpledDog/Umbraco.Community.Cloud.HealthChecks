using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Extensions;

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

            // Register folder size health checks so background job can find them
            builder.Services.AddSingleton<FolderSizeHealthCheckBase, NuGetCacheSizeHealthCheck>();
            builder.Services.AddSingleton<FolderSizeHealthCheckBase, UmbracoLogsFolderSizeHealthCheck>();

            // Register the distributed background job - runs only on elected SchedulingPublisher
            builder.Services.AddSingleton<IDistributedBackgroundJob, FolderSizeBackgroundJob>();
        }
    }
}
