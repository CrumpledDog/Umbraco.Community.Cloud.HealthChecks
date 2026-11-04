using Microsoft.Extensions.Options;

namespace Umbraco.Community.Cloud.HealthChecks.Stats;

/// <summary>Resolves and evaluates the thresholds from the <c>CloudHealthChecks</c> options.</summary>
public interface IStatsThresholds
{
    /// <summary>
    /// The thresholds for a target: configured values, or for the NuGet cache the adaptive defaults derived from the home quota.
    /// Unknown keys resolve to no thresholds.
    /// </summary>
    TargetThresholds Resolve(string targetKey);

    /// <summary>Evaluates a measurement against the target's thresholds (size, oldest file age, disk usage percentage).</summary>
    /// <param name="targetKey">The target the result belongs to.</param>
    /// <param name="result">The measurement; null yields <see cref="ThresholdLevel.None"/>.</param>
    /// <param name="nowUtc">The current time, for the age check. Defaults to <see cref="DateTime.UtcNow"/>.</param>
    ThresholdEvaluation Evaluate(string targetKey, FolderSizeResult? result, DateTime? nowUtc = null);
}

/// <inheritdoc />
public sealed class StatsThresholds : IStatsThresholds
{
    private const long FallbackNuGetErrorMb = 3072;

    private readonly CloudHealthChecksOptions _options;
    private readonly IStatsTargetCatalog _catalog;
    private readonly IDiskUsageReader _disk;

    /// <summary>Creates the evaluator.</summary>
    public StatsThresholds(IOptions<CloudHealthChecksOptions> options, IStatsTargetCatalog catalog, IDiskUsageReader disk)
    {
        _options = options.Value;
        _catalog = catalog;
        _disk = disk;
    }

    /// <inheritdoc />
    public TargetThresholds Resolve(string targetKey)
    {
        switch (targetKey.ToLowerInvariant())
        {
            case StatsTargetKeys.UmbracoLogs:
                var logs = _options.UmbracoLogs;
                return new TargetThresholds(
                    logs.WarningThresholdMb,
                    logs.ErrorThresholdMb,
                    logs.FileAgeWarningThresholdDays > 0 ? logs.FileAgeWarningThresholdDays : null,
                    logs.FileAgeErrorThresholdDays > 0 ? logs.FileAgeErrorThresholdDays : null,
                    null,
                    null);

            case StatsTargetKeys.NuGetCache:
                var error = _options.NuGetCache.ErrorThresholdMb ?? CalculateNuGetError();
                var warning = _options.NuGetCache.WarningThresholdMb ?? CalculateNuGetWarning(error);
                return new TargetThresholds(warning, error, null, null, null, null);

            case StatsTargetKeys.LocalTemp:
            case StatsTargetKeys.TempDisk:
                return new TargetThresholds(
                    null, null, null, null,
                    _options.LocalTemp.WarningThresholdPercentage,
                    _options.LocalTemp.ErrorThresholdPercentage);

            case StatsTargetKeys.HomeDisk:
            case StatsTargetKeys.LocalDisk:
                return new TargetThresholds(
                    null, null, null, null,
                    _options.AzureStorage.WarningThresholdPercentage,
                    _options.AzureStorage.ErrorThresholdPercentage);

            default:
                return new TargetThresholds(null, null, null, null, null, null);
        }
    }

    /// <inheritdoc />
    public ThresholdEvaluation Evaluate(string targetKey, FolderSizeResult? result, DateTime? nowUtc = null)
    {
        if (result == null)
        {
            return new ThresholdEvaluation(ThresholdLevel.None, Array.Empty<string>());
        }

        if (!result.Success)
        {
            return new ThresholdEvaluation(ThresholdLevel.Error, new[] { $"Measurement failed: {result.ErrorMessage}" });
        }

        var t = Resolve(targetKey);
        var level = ThresholdLevel.None;
        var reasons = new List<string>();

        void Raise(ThresholdLevel l, string reason)
        {
            if (l > level)
            {
                level = l;
            }

            reasons.Add(reason);
        }

        void Satisfied()
        {
            if (level == ThresholdLevel.None)
            {
                level = ThresholdLevel.Ok;
            }
        }

        var kind = _catalog.Find(targetKey)?.Kind ?? StatsKind.Folder;

        // Size (folders only: a disk target carries no folder size)
        if (kind == StatsKind.Folder && (t.SizeErrorMb.HasValue || t.SizeWarningMb.HasValue))
        {
            var sizeMb = result.SizeInBytes / (1024.0 * 1024.0);
            if (t.SizeErrorMb.HasValue && sizeMb >= t.SizeErrorMb.Value)
            {
                Raise(ThresholdLevel.Error, $"Exceeds error threshold of {t.SizeErrorMb.Value} MB");
            }
            else if (t.SizeWarningMb.HasValue && sizeMb >= t.SizeWarningMb.Value)
            {
                Raise(ThresholdLevel.Warning, $"Exceeds warning threshold of {t.SizeWarningMb.Value} MB");
            }
            else
            {
                Satisfied();
            }
        }

        // Age of the oldest file
        if (kind == StatsKind.Folder && (t.AgeErrorDays.HasValue || t.AgeWarningDays.HasValue))
        {
            var ageDays = result.OldestFileLastWriteTime.HasValue
                ? ((nowUtc ?? DateTime.UtcNow) - result.OldestFileLastWriteTime.Value.ToUniversalTime()).TotalDays
                : 0;

            if (t.AgeErrorDays.HasValue && ageDays >= t.AgeErrorDays.Value)
            {
                Raise(ThresholdLevel.Error, $"Oldest file age ({ageDays:F0} days) exceeds error threshold of {t.AgeErrorDays.Value} days");
            }
            else if (t.AgeWarningDays.HasValue && ageDays >= t.AgeWarningDays.Value)
            {
                Raise(ThresholdLevel.Warning, $"Oldest file age ({ageDays:F0} days) exceeds warning threshold of {t.AgeWarningDays.Value} days");
            }
            else
            {
                Satisfied();
            }
        }

        // Disk usage percentage (needs disk numbers on the result)
        if ((t.UsedErrorPercent.HasValue || t.UsedWarningPercent.HasValue) && result.DiskUsedPercent.HasValue)
        {
            var used = result.DiskUsedPercent.Value;
            if (t.UsedErrorPercent.HasValue && used >= t.UsedErrorPercent.Value)
            {
                Raise(ThresholdLevel.Error, $"Disk usage {used:F1}% exceeds error threshold of {t.UsedErrorPercent.Value}%");
            }
            else if (t.UsedWarningPercent.HasValue && used >= t.UsedWarningPercent.Value)
            {
                Raise(ThresholdLevel.Warning, $"Disk usage {used:F1}% exceeds warning threshold of {t.UsedWarningPercent.Value}%");
            }
            else
            {
                Satisfied();
            }
        }

        return new ThresholdEvaluation(level, reasons);
    }

    /// <summary>50% of the home quota rounded to the nearest GB; 3 GB when the quota cannot be read.</summary>
    private long CalculateNuGetError()
    {
        try
        {
            var homePath = _options.LocalTestMode
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : _catalog.Find(StatsTargetKeys.HomeDisk)?.Path;

            var usage = homePath == null ? null : _disk.Read(homePath);
            if (usage is { TotalBytes: > 0 } u)
            {
                var totalMb = u.TotalBytes / (1024.0 * 1024.0);
                return (long)(Math.Round(totalMb * 0.5 / 1024.0) * 1024.0);
            }
        }
        catch
        {
            // fall through to the safe default
        }

        return FallbackNuGetErrorMb;
    }

    /// <summary>75% of the error threshold rounded to the nearest GB.</summary>
    private static long CalculateNuGetWarning(long errorMb) =>
        (long)(Math.Round(errorMb * 0.75 / 1024.0) * 1024.0);
}
