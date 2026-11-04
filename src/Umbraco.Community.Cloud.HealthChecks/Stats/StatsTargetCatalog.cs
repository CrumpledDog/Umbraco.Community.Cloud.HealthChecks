using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Factories;

namespace Umbraco.Community.Cloud.HealthChecks.Stats;

/// <summary>The folders and disks this package monitors, with paths resolved for this host.</summary>
public interface IStatsTargetCatalog
{
    /// <summary>Every known target, including those that do not apply on this host (see <see cref="StatsTarget.AppliesToThisHost"/>).</summary>
    IReadOnlyList<StatsTarget> Targets { get; }

    /// <summary>This server's identity.</summary>
    ServerKey CurrentServer { get; }

    /// <summary>Finds a target by key (see <see cref="StatsTargetKeys"/>), or null.</summary>
    StatsTarget? Find(string key);
}

/// <inheritdoc />
public sealed class StatsTargetCatalog : IStatsTargetCatalog
{
    private const string HomeDirectory = @"C:\home";
    private const string LocalDirectory = @"C:\local";

    private readonly Lazy<IReadOnlyList<StatsTarget>> _targets;

    /// <summary>Creates the catalog for the running host.</summary>
    public StatsTargetCatalog(
        IHostEnvironment hostEnvironment,
        IOptions<CloudHealthChecksOptions> options,
        IMachineInfoFactory machineInfoFactory)
        : this(
            hostEnvironment.ContentRootPath,
            options.Value,
            machineInfoFactory.GetMachineIdentifier() ?? Environment.MachineName,
            new HostingEnvironmentInfo(),
            Directory.Exists)
    {
    }

    internal StatsTargetCatalog(
        string contentRoot,
        CloudHealthChecksOptions options,
        string machineId,
        HostingEnvironmentInfo environment,
        Func<string, bool> directoryExists)
    {
        CurrentServer = new ServerKey(machineId, environment.SiteKey);
        _targets = new Lazy<IReadOnlyList<StatsTarget>>(() => Build(contentRoot, options, environment, directoryExists));
    }

    /// <inheritdoc />
    public ServerKey CurrentServer { get; }

    /// <inheritdoc />
    public IReadOnlyList<StatsTarget> Targets => _targets.Value;

    /// <inheritdoc />
    public StatsTarget? Find(string key) =>
        Targets.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<StatsTarget> Build(
        string contentRoot,
        CloudHealthChecksOptions options,
        HostingEnvironmentInfo env,
        Func<string, bool> directoryExists)
    {
        var testMode = options.LocalTestMode;
        var homeExists = directoryExists(HomeDirectory);

        // Same gating the health checks always used: C:\home exists, or test mode. App Service detection (both variables) also counts.
        var onAzure = testMode || env.IsAppService || homeExists;

        var logsPath = Path.Combine(contentRoot, "umbraco", "Logs");

        var nugetPath = testMode
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            : @"C:\home\.nuget";

        var tempPath = testMode
            ? env.Temp ?? Path.GetTempPath()
            : env.Temp ?? @"D:\local\Temp";

        // The disk targets use the parent of ...\Temp (D:\local) as the path whose quota is read.
        var tempDiskPath = tempPath.EndsWith(@"\Temp", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(tempPath)?.FullName ?? tempPath
            : tempPath;

        var homeDiskPath = testMode ? contentRoot : HomeDirectory;
        var localDiskPath = testMode ? Path.Combine(contentRoot, "wwwroot") : LocalDirectory;

        var diskApplies = testMode || homeExists;
        var localDiskApplies = diskApplies && directoryExists(localDiskPath);
        var tempDiskApplies = diskApplies && directoryExists(tempDiskPath) && !SameRoot(localDiskPath, tempDiskPath);

        return new List<StatsTarget>
        {
            new(StatsTargetKeys.UmbracoLogs, "Umbraco logs", logsPath, StatsScope.Shared, StatsKind.Folder, true),
            new(StatsTargetKeys.NuGetCache, testMode ? "NuGet cache (test mode)" : "NuGet cache", nugetPath, StatsScope.Shared, StatsKind.Folder, onAzure),
            new(StatsTargetKeys.LocalTemp, "Local temp", tempPath, StatsScope.PerInstance, StatsKind.Folder, onAzure),
            new(StatsTargetKeys.HomeDisk, testMode ? "Application root (test mode)" : @"C:\home", homeDiskPath, StatsScope.Shared, StatsKind.Disk, diskApplies),
            new(StatsTargetKeys.LocalDisk, testMode ? "wwwroot folder (test mode)" : @"C:\local", localDiskPath, StatsScope.PerInstance, StatsKind.Disk, localDiskApplies),
            new(StatsTargetKeys.TempDisk, testMode ? "System temp folder (test mode)" : tempDiskPath, tempDiskPath, StatsScope.PerInstance, StatsKind.Disk, tempDiskApplies),
        };
    }

    private static bool SameRoot(string a, string b) =>
        string.Equals(RootOf(a), RootOf(b), StringComparison.OrdinalIgnoreCase);

    // A drive letter is parsed by hand so Windows style paths compare the same on any OS
    private static string? RootOf(string path) =>
        path.Length >= 2 && path[1] == ':' && char.IsLetter(path[0])
            ? path[..2]
            : Path.GetPathRoot(Path.GetFullPath(path));
}
