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
            ReturnSpecialDirectories = false
        };

        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                totalSize += file.Length;
                fileCount++;

                var written = file.LastWriteTime;
                if (oldest == null || written < oldest)
                {
                    oldest = written;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip files we can't access
            }
            catch (FileNotFoundException)
            {
                // Deleted during enumeration
            }
        }

        return new FolderSizeResult
        {
            SizeInBytes = totalSize,
            FileCount = fileCount,
            OldestFileLastWriteTime = oldest,
            CalculatedAt = nowUtc
        };
    }
}
