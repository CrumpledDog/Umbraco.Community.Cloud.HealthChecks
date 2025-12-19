using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;

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

        public UmbracoLogsFolderSizeHealthCheck(
            IHostEnvironment hostEnvironment,
            IOptions<CloudHealthChecksOptions> options)
        {
            _hostEnvironment = hostEnvironment;
            _options = options.Value;
        }

        protected override string FolderPath => Path.Combine(_hostEnvironment.ContentRootPath, "umbraco", "Logs");

        protected override long WarningThresholdMb => _options.UmbracoLogs.WarningThresholdMb;

        protected override long ErrorThresholdMb => _options.UmbracoLogs.ErrorThresholdMb;

        protected override string FolderDisplayName => "Umbraco Logs folder";

        protected override int FileAgeWarningThresholdDays => _options.UmbracoLogs.FileAgeWarningThresholdDays;

        protected override int FileAgeErrorThresholdDays => _options.UmbracoLogs.FileAgeErrorThresholdDays;
    }
}
