namespace Umbraco.Community.Cloud.HealthChecks
{
    /// <summary>
    /// Cached result of a folder size calculation
    /// </summary>
    public class FolderSizeResult
    {
        /// <summary>
        /// Total size in bytes
        /// </summary>
        public long SizeInBytes { get; set; }

        /// <summary>
        /// Total file count
        /// </summary>
        public int FileCount { get; set; }

        /// <summary>
        /// Last write time of the oldest file
        /// </summary>
        public DateTime? OldestFileLastWriteTime { get; set; }

        /// <summary>
        /// When this result was calculated
        /// </summary>
        public DateTime CalculatedAt { get; set; }

        /// <summary>
        /// Error message if calculation failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Whether the calculation completed successfully
        /// </summary>
        public bool Success => string.IsNullOrEmpty(ErrorMessage);
    }
}
