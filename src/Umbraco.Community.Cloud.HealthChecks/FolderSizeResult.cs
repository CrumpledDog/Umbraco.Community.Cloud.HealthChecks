using System.Text.Json.Serialization;

namespace Umbraco.Community.Cloud.HealthChecks
{
    /// <summary>
    /// Cached result of a folder size calculation, optionally with the usage of the disk the folder lives on.
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

        /// <summary>
        /// The monitored target this result belongs to (see <c>StatsTargetKeys</c>). Null for results written by versions before 1.4.0.
        /// </summary>
        public string? TargetKey { get; set; }

        /// <summary>
        /// Machine identifier of the server that produced the result.
        /// </summary>
        public string? MachineId { get; set; }

        /// <summary>
        /// Site key (site name plus slot, or machine name off Azure) of the server that produced the result.
        /// </summary>
        public string? SiteKey { get; set; }

        /// <summary>
        /// Friendly name of the server that produced the result (machine name plus site name).
        /// </summary>
        public string? ServerName { get; set; }

        /// <summary>
        /// Quota-aware total size of the disk behind the target, when it could be read.
        /// </summary>
        public long? DiskTotalBytes { get; set; }

        /// <summary>
        /// Quota-aware free space of the disk behind the target, when it could be read.
        /// </summary>
        public long? DiskFreeBytes { get; set; }

        /// <summary>
        /// Disk used percentage (0-100), when disk numbers are present.
        /// </summary>
        [JsonIgnore]
        public double? DiskUsedPercent =>
            DiskTotalBytes is > 0 && DiskFreeBytes.HasValue
                ? Math.Max(0, DiskTotalBytes.Value - DiskFreeBytes.Value) * 100.0 / DiskTotalBytes.Value
                : null;
    }
}
