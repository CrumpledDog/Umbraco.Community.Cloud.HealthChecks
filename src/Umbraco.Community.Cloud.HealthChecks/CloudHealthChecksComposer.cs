using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

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
        }
    }
}
