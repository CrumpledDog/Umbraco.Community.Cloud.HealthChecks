using System.Runtime.InteropServices;

namespace Umbraco.Community.Cloud.HealthChecks.Stats;

/// <summary>Reads quota-aware disk usage. Instant: it asks the OS, it never walks files.</summary>
public interface IDiskUsageReader
{
    /// <summary>
    /// Disk usage for the disk behind <paramref name="path"/>, or null when it cannot be read (path missing, OS refused).
    /// The path itself is passed to the OS, not its drive root, so App Service quotas are honoured (same numbers as Kudu).
    /// </summary>
    DiskUsage? Read(string path);
}

/// <summary>
/// Windows: <c>GetDiskFreeSpaceEx</c> on the path itself. Linux and macOS: <c>statvfs</c> on the path itself (through <see cref="DriveInfo"/>),
/// which reports the mount's size and the space available to the app (for example the quota-limited <c>/home</c> mount on Linux App Service).
/// In a plain container the figures can be those of the host disk rather than a cgroup limit.
/// </summary>
public sealed class DiskUsageReader : IDiskUsageReader
{
    /// <inheritdoc />
    public DiskUsage? Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (!Directory.Exists(path))
            {
                return null;
            }

            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ReadWindows(path) : ReadUnix(path);
        }
        catch
        {
            return null;
        }
    }

    private static DiskUsage? ReadWindows(string path) =>
        GetDiskFreeSpaceEx(path, out var free, out var total, out _) && total > 0
            ? new DiskUsage((long)total, (long)free)
            : null;

    // DriveInfo asks the OS about the path it is given (statvfs), so any folder works, not only a mount point
    private static DiskUsage? ReadUnix(string path)
    {
        var drive = new DriveInfo(path);
        var total = drive.TotalSize;
        return total > 0 ? new DiskUsage(total, drive.AvailableFreeSpace) : null;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string path, out ulong freeBytes, out ulong totalBytes, out ulong diskFreeBytes);
}
