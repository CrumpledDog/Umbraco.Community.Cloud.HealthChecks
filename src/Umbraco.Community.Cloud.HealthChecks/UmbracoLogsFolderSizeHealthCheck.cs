using Microsoft.Extensions.Hosting;
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

        public UmbracoLogsFolderSizeHealthCheck(IHostEnvironment hostEnvironment)
        {
            _hostEnvironment = hostEnvironment;
        }

        protected override string FolderPath => Path.Combine(_hostEnvironment.ContentRootPath, "umbraco", "Logs");

        protected override long WarningThresholdMb => 100; // 100 MB

        protected override long ErrorThresholdMb => 500; // 500 MB

        protected override string FolderDisplayName => "Umbraco Logs folder";

        protected override int FileAgeWarningThresholdDays => 550; // Warning after 30 days

        protected override int FileAgeErrorThresholdDays => 730; // Error after 90 days
    }
}
