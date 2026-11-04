using System.Runtime.InteropServices;

namespace Umbraco.Community.Cloud.HealthChecks.Stats;

/// <summary>Reads quota-aware disk usage. Instant: it asks the OS, it never walks files.</summary>
public interface IDiskUsageReader
{
    /// <summary>
    /// Disk usage for the disk behind <paramref name="path"/>, or null when it cannot be read (not Windows, path missing).
    /// The path itself is passed to the OS, not its drive root, so App Service quotas are honoured (same numbers as Kudu).
    /// </summary>
    DiskUsage? Read(string path);
}

/// <summary>Windows <c>GetDiskFreeSpaceEx</c> on the path itself; returns null on other platforms.</summary>
public sealed class DiskUsageReader : IDiskUsageReader
{
    /// <inheritdoc />
    public DiskUsage? Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        try
        {
            if (!Directory.Exists(path))
            {
                return null;
            }

            return GetDiskFreeSpaceEx(path, out var free, out var total, out _) && total > 0
                ? new DiskUsage((long)total, (long)free)
                : null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string path, out ulong freeBytes, out ulong totalBytes, out ulong diskFreeBytes);
}
