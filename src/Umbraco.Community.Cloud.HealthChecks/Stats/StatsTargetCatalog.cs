using Microsoft.Extensions.Configuration;
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

    /// <summary>The hosting platform detected for this site (see <see cref="HostingPlatform"/>).</summary>
    HostingPlatform Platform { get; }

    /// <summary>Finds a target by key (see <see cref="StatsTargetKeys"/>), or null.</summary>
    StatsTarget? Find(string key);
}

/// <inheritdoc />
public sealed class StatsTargetCatalog : IStatsTargetCatalog
{
    private const string HomeDirectory = @"C:\home";
    private const string LocalDirectory = @"C:\local";
    private const string LinuxHomeDirectory = "/home";

    private readonly Lazy<IReadOnlyList<StatsTarget>> _targets;

    /// <summary>Creates the catalog for the running host.</summary>
    public StatsTargetCatalog(
        IHostEnvironment hostEnvironment,
        IOptions<CloudHealthChecksOptions> options,
        IMachineInfoFactory machineInfoFactory,
        IConfiguration configuration)
        : this(
            hostEnvironment.ContentRootPath,
            options.Value,
            machineInfoFactory.GetMachineIdentifier() ?? Environment.MachineName,
            new HostingEnvironmentInfo(),
            HostingPlatformDetector.Detect(configuration, new HostingEnvironmentInfo(), hostEnvironment.ContentRootPath, File.Exists),
            Directory.Exists)
    {
    }

    internal StatsTargetCatalog(
        string contentRoot,
        CloudHealthChecksOptions options,
        string machineId,
        HostingEnvironmentInfo environment,
        HostingPlatform? platform,
        Func<string, bool> directoryExists,
        Func<string, string?>? mountOf = null)
    {
        CurrentServer = new ServerKey(machineId, environment.SiteKey);
        Platform = platform ?? (environment.IsAppService ? HostingPlatform.AzureWebApp : HostingPlatform.Other);
        _targets = new Lazy<IReadOnlyList<StatsTarget>>(() => Build(contentRoot, options, environment, Platform, directoryExists, mountOf ?? FindMountPoint));
    }

    /// <inheritdoc />
    public ServerKey CurrentServer { get; }

    /// <inheritdoc />
    public HostingPlatform Platform { get; }

    /// <inheritdoc />
    public IReadOnlyList<StatsTarget> Targets => _targets.Value;

    /// <inheritdoc />
    public StatsTarget? Find(string key) =>
        Targets.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<StatsTarget> Build(
        string contentRoot,
        CloudHealthChecksOptions options,
        HostingEnvironmentInfo env,
        HostingPlatform platform,
        Func<string, bool> directoryExists,
        Func<string, string?> mountOf)
    {
        var testMode = options.LocalTestMode;

        // Umbraco Cloud and Azure Web Apps: every target applies. Anywhere else only the logs folder does (test mode excepted).
        var onAzure = testMode || platform != HostingPlatform.Other;

        var logsPath = Path.Combine(contentRoot, "umbraco", "Logs");

        var nugetPath = testMode
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            : env.IsWindows ? @"C:\home\.nuget" : Path.Combine(LinuxHomeDirectory, ".nuget"); // on the shared /home mount, whatever HOME is, as the target is Shared

        var tempPath = testMode
            ? env.Temp ?? Path.GetTempPath()
            : env.Temp ?? (env.IsWindows ? @"D:\local\Temp" : Path.GetTempPath());

        // Windows: the disk targets use the parent of ...\Temp (D:\local) as the path whose quota is read.
        // Linux: the temp folder itself (the container's own, ephemeral, file system).
        var tempDiskPath = env.IsWindows && tempPath.EndsWith(@"\Temp", StringComparison.OrdinalIgnoreCase)
            ? tempPath[..^5]
            : TrimTrailingSeparator(tempPath);

        string homeDiskPath, localDiskPath, homeDisplayName, localDisplayName;
        if (testMode)
        {
            homeDiskPath = contentRoot;
            localDiskPath = Path.Combine(contentRoot, "wwwroot");
            homeDisplayName = "Application root (test mode)";
            localDisplayName = "wwwroot folder (test mode)";
        }
        else if (env.IsWindows)
        {
            homeDiskPath = HomeDirectory;
            localDiskPath = LocalDirectory;
            homeDisplayName = HomeDirectory;
            localDisplayName = LocalDirectory;
        }
        else
        {
            // Linux App Service: /home is the persistent, quota-limited, shared mount. There is no separate C:\local, so the
            // LocalDisk target is not used; the temp folder is reported as the per-instance disk instead.
            homeDiskPath = LinuxHomeDirectory;
            localDiskPath = tempDiskPath;
            homeDisplayName = LinuxHomeDirectory;
            localDisplayName = tempDiskPath;
        }

        var diskApplies = onAzure;
        var linuxProduction = !testMode && !env.IsWindows;
        var localDiskApplies = diskApplies && !linuxProduction && directoryExists(localDiskPath);
        var tempDiskApplies = diskApplies && directoryExists(tempDiskPath) && !(env.IsWindows
            ? SameRoot(localDiskPath, tempDiskPath)
            : SameMount(homeDiskPath, tempDiskPath, mountOf)); // every path has the root "/" on Linux, so compare mounts (test mode included)

        return new List<StatsTarget>
        {
            new(StatsTargetKeys.UmbracoLogs, "Umbraco logs", logsPath, StatsScope.Shared, StatsKind.Folder, true),
            new(StatsTargetKeys.NuGetCache, testMode ? "NuGet cache (test mode)" : "NuGet cache", nugetPath, StatsScope.Shared, StatsKind.Folder, onAzure),
            new(StatsTargetKeys.LocalTemp, "Local temp", tempPath, StatsScope.PerInstance, StatsKind.Folder, onAzure),
            new(StatsTargetKeys.HomeDisk, homeDisplayName, homeDiskPath, StatsScope.Shared, StatsKind.Disk, diskApplies),
            new(StatsTargetKeys.LocalDisk, localDisplayName, localDiskPath, StatsScope.PerInstance, StatsKind.Disk, localDiskApplies),
            new(StatsTargetKeys.TempDisk, testMode ? "System temp folder (test mode)" : tempDiskPath, tempDiskPath, StatsScope.PerInstance, StatsKind.Disk, tempDiskApplies),
        };
    }

    // Keeps "/" and "C:\" intact, drops the trailing separator Path.GetTempPath() adds ("/tmp/" -> "/tmp")
    private static string TrimTrailingSeparator(string path)
    {
        var root = Path.GetPathRoot(path);
        return path.Length > (root?.Length ?? 0) ? path.TrimEnd('/', '\\') : path;
    }

    private static bool SameRoot(string a, string b) =>
        string.Equals(RootOf(a), RootOf(b), StringComparison.OrdinalIgnoreCase);

    // A drive letter is parsed by hand so Windows style paths compare the same on any OS
    private static string? RootOf(string path) =>
        path.Length >= 2 && path[1] == ':' && char.IsLetter(path[0])
            ? path[..2]
            : Path.GetPathRoot(Path.GetFullPath(path));

    // On Linux every path has the root "/", so compare the mount points the paths live on instead.
    // When a mount cannot be determined the paths are treated as different, so the disk is reported rather than silently skipped.
    private static bool SameMount(string a, string b, Func<string, string?> mountOf)
    {
        var mountA = mountOf(a);
        var mountB = mountOf(b);
        return mountA != null && mountB != null && string.Equals(mountA, mountB, StringComparison.Ordinal);
    }

    /// <summary>The mount point (longest matching drive root) the path lives on, or null when it cannot be found.</summary>
    internal static string? FindMountPoint(string path)
    {
        try
        {
            return FindMountPoint(ResolveSymlinks(path), DriveInfo.GetDrives().Select(d => d.RootDirectory.FullName));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Full path with every symlinked component replaced by its target, so a link into another mount is matched to that mount.</summary>
    internal static string ResolveSymlinks(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;
        foreach (var segment in full[root.Length..].Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                var target = new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true);
                if (target != null)
                {
                    current = target.FullName;
                }
            }
            catch (IOException)
            {
                // not a link we can follow (missing, circular): keep the path as it is
            }
        }

        return current;
    }

    /// <summary>The longest of <paramref name="mounts"/> that contains the path. A mount must match a whole directory name: /home does not contain /home2/tmp.</summary>
    internal static string? FindMountPoint(string path, IEnumerable<string> mounts)
    {
        var full = Path.GetFullPath(path);
        return mounts
            .Where(root => IsUnder(full, root))
            .OrderByDescending(root => root.Length)
            .FirstOrDefault();
    }

    private static bool IsUnder(string full, string root)
    {
        if (string.Equals(full, root, StringComparison.Ordinal))
        {
            return true;
        }

        var prefix = root.EndsWith('/') || root.EndsWith('\\') ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.Ordinal);
    }
}
