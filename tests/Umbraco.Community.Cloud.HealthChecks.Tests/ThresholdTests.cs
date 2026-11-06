using Microsoft.Extensions.Options;
using Umbraco.Community.Cloud.HealthChecks.Stats;
using Xunit;

namespace Umbraco.Community.Cloud.HealthChecks.Tests;

public class ThresholdTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static StatsThresholds Create(CloudHealthChecksOptions? options = null, DiskUsage? quota = null)
    {
        var root = Path.GetTempPath();
        var o = options ?? new CloudHealthChecksOptions();
        var catalog = Build.TestModeCatalog(root);
        return new StatsThresholds(Options.Create(o), catalog, new FakeDiskReader { Usage = quota });
    }

    private static CloudHealthChecksOptions Real() => new() { LocalTestMode = false };

    // --- logs

    [Fact]
    public void Logs_size_below_warning_is_ok()
    {
        var e = Create().Evaluate(StatsTargetKeys.UmbracoLogs, new FolderSizeResult { SizeInBytes = 100L * 1024 * 1024, CalculatedAt = Now }, Now);

        Assert.Equal(ThresholdLevel.Ok, e.Level);
        Assert.Empty(e.Reasons);
    }

    [Theory]
    [InlineData(250, ThresholdLevel.Warning)]
    [InlineData(499, ThresholdLevel.Warning)]
    [InlineData(500, ThresholdLevel.Error)]
    public void Logs_size_thresholds_are_250_and_500_mb(long mb, ThresholdLevel expected)
    {
        var e = Create().Evaluate(StatsTargetKeys.UmbracoLogs, new FolderSizeResult { SizeInBytes = mb * 1024 * 1024, CalculatedAt = Now }, Now);

        Assert.Equal(expected, e.Level);
        Assert.Single(e.Reasons);
        Assert.Contains("threshold", e.Reasons[0]);
    }

    [Theory]
    [InlineData(549, ThresholdLevel.Ok)]
    [InlineData(550, ThresholdLevel.Warning)]
    [InlineData(729, ThresholdLevel.Warning)]
    [InlineData(730, ThresholdLevel.Error)]
    public void Logs_oldest_file_age_thresholds_are_550_and_730_days(int days, ThresholdLevel expected)
    {
        var e = Create().Evaluate(
            StatsTargetKeys.UmbracoLogs,
            new FolderSizeResult { OldestFileLastWriteTime = Now.AddDays(-days - 0.01), CalculatedAt = Now },
            Now);

        Assert.Equal(expected, e.Level);
    }

    [Fact]
    public void Worst_level_wins_and_all_reasons_are_listed()
    {
        var e = Create().Evaluate(
            StatsTargetKeys.UmbracoLogs,
            new FolderSizeResult { SizeInBytes = 300L * 1024 * 1024, OldestFileLastWriteTime = Now.AddDays(-800), CalculatedAt = Now },
            Now);

        Assert.Equal(ThresholdLevel.Error, e.Level);
        Assert.Equal(2, e.Reasons.Count);
        Assert.Contains("Oldest file age", e.Reasons[1]);
    }

    [Fact]
    public void Configured_logs_thresholds_override_defaults()
    {
        var o = new CloudHealthChecksOptions { UmbracoLogs = { WarningThresholdMb = 1, ErrorThresholdMb = 2, FileAgeWarningThresholdDays = 0, FileAgeErrorThresholdDays = 0 } };
        var t = Create(o);

        Assert.Equal(ThresholdLevel.Error, t.Evaluate(StatsTargetKeys.UmbracoLogs, new FolderSizeResult { SizeInBytes = 3L * 1024 * 1024 }, Now).Level);
        Assert.Null(t.Resolve(StatsTargetKeys.UmbracoLogs).AgeErrorDays);
    }

    // --- adaptive NuGet

    [Fact]
    public void NuGet_error_is_half_the_home_quota_rounded_to_gb_and_warning_is_75_percent_of_that()
    {
        // 100 GB quota -> error 50 GB; warning 37.5 GB rounds to 38 GB
        var t = Create(Real(), new DiskUsage(100 * Gb, 90 * Gb)).Resolve(StatsTargetKeys.NuGetCache);

        Assert.Equal(50 * 1024, t.SizeErrorMb);
        Assert.Equal(38 * 1024, t.SizeWarningMb);
    }

    [Fact]
    public void NuGet_adaptive_rounding_for_a_6gb_quota()
    {
        // 6 GB -> 3 GB error; 75% = 2.25 GB -> 2 GB warning
        var t = Create(Real(), new DiskUsage(6 * Gb, 1 * Gb)).Resolve(StatsTargetKeys.NuGetCache);

        Assert.Equal(3072, t.SizeErrorMb);
        Assert.Equal(2048, t.SizeWarningMb);
    }

    [Fact]
    public void NuGet_falls_back_to_3gb_when_the_quota_cannot_be_read()
    {
        var t = Create(Real(), null).Resolve(StatsTargetKeys.NuGetCache);

        Assert.Equal(3072, t.SizeErrorMb);
        Assert.Equal(2048, t.SizeWarningMb);
    }

    [Fact]
    public void NuGet_configured_error_wins_and_warning_derives_from_it()
    {
        var o = Real();
        o.NuGetCache.ErrorThresholdMb = 8192;
        var t = Create(o, new DiskUsage(100 * Gb, 0)).Resolve(StatsTargetKeys.NuGetCache);

        Assert.Equal(8192, t.SizeErrorMb);
        Assert.Equal(6144, t.SizeWarningMb);
    }

    [Fact]
    public void NuGet_configured_values_are_used_verbatim()
    {
        var o = Real();
        o.NuGetCache.ErrorThresholdMb = 900;
        o.NuGetCache.WarningThresholdMb = 500;
        var t = Create(o, new DiskUsage(100 * Gb, 0));

        Assert.Equal(ThresholdLevel.Ok, t.Evaluate(StatsTargetKeys.NuGetCache, new FolderSizeResult { SizeInBytes = 499L * 1024 * 1024 }, Now).Level);
        Assert.Equal(ThresholdLevel.Warning, t.Evaluate(StatsTargetKeys.NuGetCache, new FolderSizeResult { SizeInBytes = 500L * 1024 * 1024 }, Now).Level);
        Assert.Equal(ThresholdLevel.Error, t.Evaluate(StatsTargetKeys.NuGetCache, new FolderSizeResult { SizeInBytes = 900L * 1024 * 1024 }, Now).Level);
    }

    [Fact]
    public void NuGet_evaluation_uses_the_adaptive_thresholds()
    {
        var t = Create(Real(), new DiskUsage(6 * Gb, 1 * Gb));

        Assert.Equal(ThresholdLevel.Ok, t.Evaluate(StatsTargetKeys.NuGetCache, new FolderSizeResult { SizeInBytes = 1 * Gb }, Now).Level);
        Assert.Equal(ThresholdLevel.Warning, t.Evaluate(StatsTargetKeys.NuGetCache, new FolderSizeResult { SizeInBytes = 2 * Gb }, Now).Level);
        Assert.Equal(ThresholdLevel.Error, t.Evaluate(StatsTargetKeys.NuGetCache, new FolderSizeResult { SizeInBytes = 3 * Gb }, Now).Level);
    }

    // --- percentages

    private static FolderSizeResult Disk(double percent) =>
        new() { DiskTotalBytes = 1000, DiskFreeBytes = (long)(1000 - percent * 10) };

    [Theory]
    [InlineData(50, ThresholdLevel.Ok)]
    [InlineData(75, ThresholdLevel.Warning)]
    [InlineData(89, ThresholdLevel.Warning)]
    [InlineData(90, ThresholdLevel.Error)]
    public void Home_and_local_disks_use_the_azure_storage_percentages(double percent, ThresholdLevel expected)
    {
        var t = Create();

        Assert.Equal(expected, t.Evaluate(StatsTargetKeys.HomeDisk, Disk(percent), Now).Level);
        Assert.Equal(expected, t.Evaluate(StatsTargetKeys.LocalDisk, Disk(percent), Now).Level);
    }

    [Theory]
    [InlineData(65, ThresholdLevel.Ok)]
    [InlineData(66, ThresholdLevel.Warning)]
    [InlineData(90, ThresholdLevel.Error)]
    public void Temp_disk_and_local_temp_folder_use_the_local_temp_percentages(double percent, ThresholdLevel expected)
    {
        var t = Create();

        Assert.Equal(expected, t.Evaluate(StatsTargetKeys.TempDisk, Disk(percent), Now).Level);
        Assert.Equal(expected, t.Evaluate(StatsTargetKeys.LocalTemp, Disk(percent), Now).Level);
    }

    [Fact]
    public void Percentages_come_from_the_options()
    {
        var o = new CloudHealthChecksOptions { AzureStorage = { WarningThresholdPercentage = 10, ErrorThresholdPercentage = 20 } };

        var e = Create(o).Evaluate(StatsTargetKeys.HomeDisk, Disk(15), Now);

        Assert.Equal(ThresholdLevel.Warning, e.Level);
        Assert.Contains("15.0%", e.Reasons[0]);
        Assert.Contains("10%", e.Reasons[0]);
    }

    [Fact]
    public void Disk_numbers_missing_means_no_disk_evaluation()
    {
        Assert.Equal(ThresholdLevel.None, Create().Evaluate(StatsTargetKeys.HomeDisk, new FolderSizeResult(), Now).Level);
    }

    // --- None / failures

    [Fact]
    public void No_result_is_none()
    {
        var e = Create().Evaluate(StatsTargetKeys.UmbracoLogs, null);

        Assert.Equal(ThresholdLevel.None, e.Level);
    }

    [Fact]
    public void Unknown_target_has_no_thresholds()
    {
        var t = Create();

        Assert.Equal(ThresholdLevel.None, t.Evaluate("custom", new FolderSizeResult { SizeInBytes = 999 * Gb }, Now).Level);
    }

    [Fact]
    public void Failed_measurement_is_an_error_with_the_message()
    {
        var e = Create().Evaluate(StatsTargetKeys.UmbracoLogs, new FolderSizeResult { ErrorMessage = "took longer than the limit, will retry" }, Now);

        Assert.Equal(ThresholdLevel.Error, e.Level);
        Assert.Contains("will retry", e.Reasons[0]);
    }

    [Fact]
    public void Folders_without_size_thresholds_such_as_local_temp_are_not_size_evaluated()
    {
        var e = Create().Evaluate(StatsTargetKeys.LocalTemp, new FolderSizeResult { SizeInBytes = 500 * Gb }, Now);

        Assert.Equal(ThresholdLevel.None, e.Level);
    }
}
