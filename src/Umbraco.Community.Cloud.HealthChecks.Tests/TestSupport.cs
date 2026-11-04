using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.Community.Cloud.HealthChecks.Services;
using Umbraco.Community.Cloud.HealthChecks.Stats;

namespace Umbraco.Community.Cloud.HealthChecks.Tests;

internal sealed class InMemoryResultStore : IHealthCheckResultStore
{
    public Dictionary<string, FolderSizeResult> Items { get; } = new();

    public Task StoreAsync(string healthCheckName, FolderSizeResult result)
    {
        Items[healthCheckName] = result;
        return Task.CompletedTask;
    }

    public Task<FolderSizeResult?> GetAsync(string healthCheckName) =>
        Task.FromResult(Items.TryGetValue(healthCheckName, out var r) ? r : null);
}

internal sealed class FakeDiskReader : IDiskUsageReader
{
    public DiskUsage? Usage { get; set; }

    public int Reads { get; private set; }

    public DiskUsage? Read(string path)
    {
        Reads++;
        return Usage;
    }
}

internal sealed class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";

    public string ApplicationName { get; set; } = "Test";

    public string ContentRootPath { get; set; } = "";

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string AddFile(string relative, int bytes, DateTime? lastWrite = null)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[bytes]);
        if (lastWrite.HasValue)
        {
            File.SetLastWriteTime(full, lastWrite.Value);
        }

        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch
        {
            // best effort
        }
    }
}

internal static class Build
{
    public static HostingEnvironmentInfo Azure(string site = "mysite", string? slot = null, string instance = "inst1") =>
        new(name => name switch
        {
            "WEBSITE_SITE_NAME" => site,
            "WEBSITE_INSTANCE_ID" => instance,
            "WEBSITE_SLOT_NAME" => slot,
            _ => null
        }, "MACHINE");

    public static HostingEnvironmentInfo OffAzure(string machine = "MACHINE") => new(_ => null, machine);

    /// <summary>A catalog in LocalTestMode rooted at a temp folder, as an Azure-less dev machine would see it.</summary>
    public static StatsTargetCatalog TestModeCatalog(string contentRoot, string machineId = "m1", string site = "site1", string? tempOverride = null)
    {
        var env = new HostingEnvironmentInfo(
            name => name == "TEMP" ? tempOverride : null,
            site);
        return new StatsTargetCatalog(contentRoot, new CloudHealthChecksOptions { LocalTestMode = true }, machineId, env, null, Directory.Exists);
    }

    public static StatsService Service(
        IHealthCheckResultStore store,
        IStatsTargetCatalog catalog,
        IDiskUsageReader? disk = null,
        CloudHealthChecksOptions? options = null) =>
        new(store, catalog, disk ?? new FakeDiskReader(), Options.Create(options ?? new CloudHealthChecksOptions()), NullLogger<StatsService>.Instance);
}
