namespace Umbraco.Community.Cloud.HealthChecks
{
    /// <summary>
    /// Configuration options for Umbraco Cloud Health Checks
    /// </summary>
    public class CloudHealthChecksOptions
    {
        /// <summary>
        /// Configuration section name in appsettings.json
        /// </summary>
        public const string SectionName = "CloudHealthChecks";

        /// <summary>
        /// Enable local test mode to test health checks outside of Azure environment.
        /// When enabled, uses local paths for testing Azure and NuGet health checks.
        /// </summary>
        public bool LocalTestMode { get; set; } = false;

        /// <summary>
        /// Options for Azure Storage health check
        /// </summary>
        public AzureStorageOptions AzureStorage { get; set; } = new();

        /// <summary>
        /// Options for NuGet Cache health check
        /// </summary>
        public NuGetCacheOptions NuGetCache { get; set; } = new();

        /// <summary>
        /// Options for Umbraco Logs health check
        /// </summary>
        public UmbracoLogsOptions UmbracoLogs { get; set; } = new();

        public class AzureStorageOptions
        {
            /// <summary>
            /// Warning threshold percentage for storage usage (default: 75%)
            /// </summary>
            public double WarningThresholdPercentage { get; set; } = 75.0;

            /// <summary>
            /// Error threshold percentage for storage usage (default: 90%)
            /// </summary>
            public double ErrorThresholdPercentage { get; set; } = 90.0;
        }

        public class NuGetCacheOptions
        {
            /// <summary>
            /// Warning threshold in MB for NuGet cache size (default: 2560 MB / 2.5 GB)
            /// </summary>
            public long WarningThresholdMb { get; set; } = 2560;

            /// <summary>
            /// Error threshold in MB for NuGet cache size (default: 3072 MB / 3 GB)
            /// </summary>
            public long ErrorThresholdMb { get; set; } = 3072;
        }

        public class UmbracoLogsOptions
        {
            /// <summary>
            /// Warning threshold in MB for logs folder size (default: 100 MB)
            /// </summary>
            public long WarningThresholdMb { get; set; } = 100;

            /// <summary>
            /// Error threshold in MB for logs folder size (default: 500 MB)
            /// </summary>
            public long ErrorThresholdMb { get; set; } = 500;

            /// <summary>
            /// Warning threshold in days for old log files (default: 550 days)
            /// </summary>
            public int FileAgeWarningThresholdDays { get; set; } = 550;

            /// <summary>
            /// Error threshold in days for old log files (default: 730 days)
            /// </summary>
            public int FileAgeErrorThresholdDays { get; set; } = 730;
        }
    }
}
