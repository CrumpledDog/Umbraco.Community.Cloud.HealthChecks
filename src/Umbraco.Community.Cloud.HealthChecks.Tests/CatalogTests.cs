using Umbraco.Community.Cloud.HealthChecks.Stats;
using Xunit;

namespace Umbraco.Community.Cloud.HealthChecks.Tests;

public class HostingEnvironmentTests
{
    [Fact]
    public void Azure_needs_both_site_name_and_instance_id()
    {
        Assert.True(Build.Azure().IsAppService);
        Assert.False(new HostingEnvironmentInfo(n => n == "WEBSITE_SITE_NAME" ? "s" : null, "M").IsAppService);
        Assert.False(new HostingEnvironmentInfo(n => n == "WEBSITE_INSTANCE_ID" ? "i" : null, "M").IsAppService);
    }

    [Fact]
    public void Site_key_is_site_name_plus_slot()
    {
        Assert.Equal("mysite", Build.Azure("mysite").SiteKey);
        Assert.Equal("mysite:staging", Build.Azure("mysite", "staging").SiteKey);
    }

    [Fact]
    public void Site_key_off_azure_is_the_machine_name()
    {
        Assert.Equal("DEV-PC", Build.OffAzure("DEV-PC").SiteKey);
    }
}

public class CatalogTests
{
    [Fact]
    public void Scopes_and_kinds_match_the_model()
    {
        using var root = new TempFolder();
        var catalog = Build.TestModeCatalog(root.Path);

        Assert.Equal(StatsScope.Shared, catalog.Find(StatsTargetKeys.UmbracoLogs)!.Scope);
        Assert.Equal(StatsScope.Shared, catalog.Find(StatsTargetKeys.NuGetCache)!.Scope);
        Assert.Equal(StatsScope.PerInstance, catalog.Find(StatsTargetKeys.LocalTemp)!.Scope);
        Assert.Equal(StatsScope.Shared, catalog.Find(StatsTargetKeys.HomeDisk)!.Scope);
        Assert.Equal(StatsScope.PerInstance, catalog.Find(StatsTargetKeys.LocalDisk)!.Scope);
        Assert.Equal(StatsKind.Folder, catalog.Find(StatsTargetKeys.NuGetCache)!.Kind);
        Assert.Equal(StatsKind.Disk, catalog.Find(StatsTargetKeys.HomeDisk)!.Kind);
    }

    [Fact]
    public void Current_server_combines_machine_id_and_site_key()
    {
        using var root = new TempFolder();
        var catalog = Build.TestModeCatalog(root.Path, "machine-7", "site-a");

        Assert.Equal(new ServerKey("machine-7", "site-a"), catalog.CurrentServer);
    }

    [Fact]
    public void Logs_path_is_under_content_root()
    {
        using var root = new TempFolder();
        var catalog = Build.TestModeCatalog(root.Path);

        Assert.Equal(Path.Combine(root.Path, "umbraco", "Logs"), catalog.Find(StatsTargetKeys.UmbracoLogs)!.Path);
        Assert.True(catalog.Find(StatsTargetKeys.UmbracoLogs)!.AppliesToThisHost);
    }

    [Fact]
    public void Azure_targets_do_not_apply_off_azure_without_test_mode()
    {
        using var root = new TempFolder();
        var catalog = new StatsTargetCatalog(root.Path, new CloudHealthChecksOptions(), "m", Build.OffAzure(), _ => false);

        Assert.True(catalog.Find(StatsTargetKeys.UmbracoLogs)!.AppliesToThisHost);
        Assert.False(catalog.Find(StatsTargetKeys.NuGetCache)!.AppliesToThisHost);
        Assert.False(catalog.Find(StatsTargetKeys.LocalTemp)!.AppliesToThisHost);
        Assert.False(catalog.Find(StatsTargetKeys.HomeDisk)!.AppliesToThisHost);
    }

    [Fact]
    public void App_service_applies_folder_targets()
    {
        using var root = new TempFolder();
        var catalog = new StatsTargetCatalog(root.Path, new CloudHealthChecksOptions(), "m", Build.Azure(), _ => false);

        Assert.True(catalog.Find(StatsTargetKeys.NuGetCache)!.AppliesToThisHost);
        Assert.True(catalog.Find(StatsTargetKeys.LocalTemp)!.AppliesToThisHost);
    }

    [Fact]
    public void Home_present_means_azure_disks_apply_and_temp_disk_needs_a_different_drive()
    {
        using var root = new TempFolder();
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\home", @"C:\local", @"D:\local" };
        var env = new HostingEnvironmentInfo(n => n == "TEMP" ? @"D:\local\Temp" : null, "m");
        var catalog = new StatsTargetCatalog(root.Path, new CloudHealthChecksOptions(), "m", env, existing.Contains);

        Assert.True(catalog.Find(StatsTargetKeys.HomeDisk)!.AppliesToThisHost);
        Assert.True(catalog.Find(StatsTargetKeys.LocalDisk)!.AppliesToThisHost);
        Assert.True(catalog.Find(StatsTargetKeys.TempDisk)!.AppliesToThisHost);
        Assert.Equal(@"D:\local", catalog.Find(StatsTargetKeys.TempDisk)!.Path);

        // Same drive as C:\local: not reported twice
        var same = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\home", @"C:\local", @"C:\local\x" };
        var env2 = new HostingEnvironmentInfo(n => n == "TEMP" ? @"C:\local\x" : null, "m");
        var catalog2 = new StatsTargetCatalog(root.Path, new CloudHealthChecksOptions(), "m", env2, same.Contains);
        Assert.False(catalog2.Find(StatsTargetKeys.TempDisk)!.AppliesToThisHost);
    }
}
