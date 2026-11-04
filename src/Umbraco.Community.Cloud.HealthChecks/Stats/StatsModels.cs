namespace Umbraco.Community.Cloud.HealthChecks.Stats;

/// <summary>Whether a monitored location is one disk shared by every instance of a web app, or local to each instance.</summary>
public enum StatsScope
{
    /// <summary>One disk shared by all instances of a site (for example <c>C:\home</c>: logs, NuGet cache). Results are keyed by <see cref="ServerKey.SiteKey"/>.</summary>
    Shared = 0,

    /// <summary>Local to each worker (for example <c>D:\local</c> temp). Results are keyed by <see cref="ServerKey.MachineId"/>.</summary>
    PerInstance = 1
}

/// <summary>What is measured at a target.</summary>
public enum StatsKind
{
    /// <summary>A folder: size, file count and oldest file (one traversal).</summary>
    Folder = 0,

    /// <summary>A disk: quota-aware total/free space, never a scan.</summary>
    Disk = 1
}

/// <summary>Outcome of evaluating a result against thresholds.</summary>
public enum ThresholdLevel
{
    /// <summary>No thresholds apply, or there is no result to evaluate.</summary>
    None = 0,

    /// <summary>Thresholds apply and none are exceeded.</summary>
    Ok = 1,

    /// <summary>A warning threshold is exceeded.</summary>
    Warning = 2,

    /// <summary>An error threshold is exceeded, or the measurement failed.</summary>
    Error = 3
}

/// <summary>Identifies a server: its machine id (per instance) and its site key (the disk all instances of a web app share).</summary>
/// <param name="MachineId">From <c>IMachineInfoFactory.GetMachineIdentifier()</c>.</param>
/// <param name="SiteKey"><c>WEBSITE_SITE_NAME</c> plus <c>WEBSITE_SLOT_NAME</c> on Azure App Service; the machine name elsewhere.</param>
public readonly record struct ServerKey(string MachineId, string SiteKey);

/// <summary>A location the package monitors.</summary>
/// <param name="Key">Stable key, see <see cref="StatsTargetKeys"/>.</param>
/// <param name="DisplayName">Human readable name.</param>
/// <param name="Path">Resolved path on this host (folder, or the path whose disk is read).</param>
/// <param name="Scope">Shared by all instances of the site, or per instance.</param>
/// <param name="Kind">Folder scan or disk usage.</param>
/// <param name="AppliesToThisHost">False when the target does not exist here (not on Azure, folder absent) and test mode is off.</param>
public sealed record StatsTarget(
    string Key,
    string DisplayName,
    string Path,
    StatsScope Scope,
    StatsKind Kind,
    bool AppliesToThisHost);

/// <summary>Well-known <see cref="StatsTarget.Key"/> values.</summary>
public static class StatsTargetKeys
{
    /// <summary><c>{ContentRoot}/umbraco/Logs</c>. Folder, shared.</summary>
    public const string UmbracoLogs = "umbraco-logs";

    /// <summary><c>C:\home\.nuget</c>. Folder, shared.</summary>
    public const string NuGetCache = "nuget-cache";

    /// <summary>The worker's temp folder (normally <c>D:\local\Temp</c>). Folder, per instance.</summary>
    public const string LocalTemp = "local-temp";

    /// <summary><c>C:\home</c> quota. Disk, shared.</summary>
    public const string HomeDisk = "home-disk";

    /// <summary><c>C:\local</c> quota. Disk, per instance.</summary>
    public const string LocalDisk = "local-disk";

    /// <summary>The temp drive (normally <c>D:\local</c>) when it is a different drive from <c>C:\local</c>. Disk, per instance.</summary>
    public const string TempDisk = "temp-disk";
}

/// <summary>Quota-aware space of the disk behind a path.</summary>
/// <param name="TotalBytes">Total size (the quota on App Service).</param>
/// <param name="FreeBytes">Free space available to the caller.</param>
public readonly record struct DiskUsage(long TotalBytes, long FreeBytes)
{
    /// <summary>Used bytes.</summary>
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    /// <summary>Used percentage, 0-100.</summary>
    public double UsedPercent => TotalBytes <= 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
}

/// <summary>The thresholds that apply to a target. A null value means that check does not apply.</summary>
public sealed record TargetThresholds(
    long? SizeWarningMb,
    long? SizeErrorMb,
    int? AgeWarningDays,
    int? AgeErrorDays,
    double? UsedWarningPercent,
    double? UsedErrorPercent);

/// <summary>Result of evaluating a measurement against its thresholds.</summary>
/// <param name="Level">Worst level reached.</param>
/// <param name="Reasons">Human readable reasons for every threshold exceeded (empty when none).</param>
public sealed record ThresholdEvaluation(ThresholdLevel Level, IReadOnlyList<string> Reasons);

/// <summary>Where the site is hosted, as far as it can be detected.</summary>
public enum HostingPlatform
{
    /// <summary>Neither Umbraco Cloud nor Azure App Service: only the logs folder is monitored.</summary>
    Other = 0,

    /// <summary>Azure App Service (both <c>WEBSITE_SITE_NAME</c> and <c>WEBSITE_INSTANCE_ID</c> are set), not Umbraco Cloud.</summary>
    AzureWebApp = 1,

    /// <summary>Umbraco Cloud.</summary>
    UmbracoCloud = 2
}
