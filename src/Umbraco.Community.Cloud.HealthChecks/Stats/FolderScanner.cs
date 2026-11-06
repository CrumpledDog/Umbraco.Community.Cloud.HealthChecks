using System.IO.Enumeration;

namespace Umbraco.Community.Cloud.HealthChecks.Stats;

/// <summary>Single-pass folder traversal (size, file count, oldest file) that honours a cancellation token.</summary>
internal static class FolderScanner
{
    /// <summary>Scans <paramref name="path"/>. Throws <see cref="OperationCanceledException"/> when the token is cancelled; never returns a partial result.</summary>
    public static FolderSizeResult Scan(string path, DateTime nowUtc, CancellationToken cancellationToken)
    {
        long totalSize = 0;
        var fileCount = 0;
        DateTime? oldest = null;

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.None // the default skips Hidden, which on Linux means every dot-file and dot-folder
        };

        // Every entry, directories included, passes the predicate, so cancellation is observed even in a large tree of empty folders
        var files = new FileSystemEnumerable<(long Length, DateTime LastWrite)>(
            path,
            (ref FileSystemEntry entry) => (entry.Length, entry.LastWriteTimeUtc.UtcDateTime.ToLocalTime()),
            enumerationOptions)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return !entry.IsDirectory;
            }
        };

        foreach (var file in files)
        {
            totalSize += file.Length;
            fileCount++;

            if (oldest == null || file.LastWrite < oldest)
            {
                oldest = file.LastWrite;
            }
        }

        // The deadline may expire during the last entries: never report that as success
        cancellationToken.ThrowIfCancellationRequested();

        return new FolderSizeResult
        {
            SizeInBytes = totalSize,
            FileCount = fileCount,
            OldestFileLastWriteTime = oldest,
            CalculatedAt = nowUtc
        };
    }
}
