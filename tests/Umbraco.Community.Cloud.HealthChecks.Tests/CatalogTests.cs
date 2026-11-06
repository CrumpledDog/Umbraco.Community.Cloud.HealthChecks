using Microsoft.Extensions.Configuration;
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
        var catalog = new StatsTargetCatalog(root.Path, new CloudHealthChecksOptions(), "m", Build.OffAzure(), null, _ => false);

        Assert.True(catalog.Find(StatsTargetKeys.UmbracoLogs)!.AppliesToThisHost);
        Assert.False(catalog.Find(StatsTargetKeys.NuGetCache)!.AppliesToThisHost);
        Assert.False(catalog.Find(StatsTargetKeys.LocalTemp)!.AppliesToThisHost);
        Assert.False(catalog.Find(StatsTargetKeys.HomeDisk)!.AppliesToThisHost);
    }

    [Fact]
    public void App_service_applies_folder_targets()
    {
        using var root = new TempFolder();
        var catalog = new StatsTargetCatalog(root.Path, new CloudHealthChecksOptions(), "m", Build.Azure(), null, _ => false);

        Assert.True(catalog.Find(StatsTargetKeys.NuGetCache)!.AppliesToThisHost);
        Assert.True(catalog.Find(StatsTargetKeys.LocalTemp)!.AppliesToThisHost);
    }

    [Fact]
    public void Home_present_means_azure_disks_apply_and_temp_disk_needs_a_different_drive()
    {
        using var root = new TempFolder();
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\home", @"C:\local", @"D:\local" };
        var env = new HostingEnvironmentInfo(n => n == "TEMP" ? @"D:\local\Temp" : null, "m", isWindows: true);
        var catalog = new StatsTargetCatalog(root.Path, new CloudHealthChecksOptions(), "m", env, HostingPlatform.AzureWebApp, existing.Contains);

        Assert.True(catalog.Find(StatsTargetKeys.HomeDisk)!.AppliesToThisHost);
        Assert.True(catalog.Find(StatsTargetKeys.LocalDisk)!.AppliesToThisHost);
        Assert.True(catalog.Find(StatsTargetKeys.TempDisk)!.AppliesToThisHost);
        Assert.Equal(@"D:\local", catalog.Find(StatsTargetKeys.TempDisk)!.Path);

        // Same drive as C:\local: not reported twice
        var same = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\home", @"C:\local", @"C:\local\x" };
        var env2 = new HostingEnvironmentInfo(n => n == "TEMP" ? @"C:\local\x" : null, "m", isWindows: true);
        var catalog2 = new StatsTargetCatalog(root.Path, new CloudHealthChecksOptions(), "m", env2, HostingPlatform.AzureWebApp, same.Contains);
        Assert.False(catalog2.Find(StatsTargetKeys.TempDisk)!.AppliesToThisHost);
    }
}

public class LinuxCatalogTests
{
    private static StatsTargetCatalog Catalog(
        HostingEnvironmentInfo env,
        Func<string, bool>? exists = null,
        Func<string, string?>? mountOf = null,
        string contentRoot = "/home/site/wwwroot") =>
        new(contentRoot, new CloudHealthChecksOptions(), "m", env, HostingPlatform.AzureWebApp, exists ?? (_ => true), mountOf ?? (p => p.StartsWith("/home") ? "/home" : "/"));

    [Fact]
    public void Home_disk_is_slash_home_and_nuget_cache_sits_under_home()
    {
        var catalog = Catalog(Build.LinuxAzure(("HOME", "/home")));

        var home = catalog.Find(StatsTargetKeys.HomeDisk)!;
        Assert.Equal("/home", home.Path);
        Assert.Equal("/home", home.DisplayName);
        Assert.True(home.AppliesToThisHost);
        Assert.Equal(Path.Combine("/home", ".nuget"), catalog.Find(StatsTargetKeys.NuGetCache)!.Path);
    }

    [Fact]
    public void Temp_comes_from_tmpdir_without_a_trailing_separator()
    {
        var catalog = Catalog(Build.LinuxAzure(("TMPDIR", "/var/tmp/")));

        Assert.Equal("/var/tmp/", catalog.Find(StatsTargetKeys.LocalTemp)!.Path);
        Assert.Equal("/var/tmp", catalog.Find(StatsTargetKeys.TempDisk)!.Path);
    }

    [Fact]
    public void There_is_no_local_disk_on_linux_and_the_temp_disk_is_reported_when_it_is_a_different_mount()
    {
        var catalog = Catalog(Build.LinuxAzure(("TMPDIR", "/tmp")));

        Assert.False(catalog.Find(StatsTargetKeys.LocalDisk)!.AppliesToThisHost);
        Assert.True(catalog.Find(StatsTargetKeys.TempDisk)!.AppliesToThisHost); // /tmp is on "/", /home on its own mount
    }

    [Fact]
    public void Temp_disk_on_the_same_mount_as_home_is_not_reported_twice()
    {
        var catalog = Catalog(Build.LinuxAzure(("TMPDIR", "/home/tmp")));

        Assert.False(catalog.Find(StatsTargetKeys.TempDisk)!.AppliesToThisHost);
    }

    [Fact]
    public void Unknown_mounts_are_treated_as_different_so_the_disk_is_still_reported()
    {
        var catalog = Catalog(Build.LinuxAzure(("TMPDIR", "/tmp")), mountOf: _ => null);

        Assert.True(catalog.Find(StatsTargetKeys.TempDisk)!.AppliesToThisHost);
    }

    [Fact]
    public void Missing_temp_folder_means_the_temp_disk_does_not_apply()
    {
        var catalog = Catalog(Build.LinuxAzure(("TMPDIR", "/tmp")), exists: p => p != "/tmp");

        Assert.False(catalog.Find(StatsTargetKeys.TempDisk)!.AppliesToThisHost);
    }

    [Fact]
    public void Linux_hosts_that_are_not_app_service_only_monitor_the_logs()
    {
        var catalog = new StatsTargetCatalog("/app", new CloudHealthChecksOptions(), "m", Build.OffAzure(isWindows: false), null, _ => true, _ => "/");

        Assert.Equal(new[] { StatsTargetKeys.UmbracoLogs }, catalog.Targets.Where(t => t.AppliesToThisHost).Select(t => t.Key));
    }

    [Fact]
    public void Temp_comes_from_the_os_specific_variable_not_the_other_ones()
    {
        var linux = new HostingEnvironmentInfo(n => n switch { "TEMP" => "/wrong", "TMPDIR" => "/right", _ => null }, "m", isWindows: false);
        var windows = new HostingEnvironmentInfo(n => n switch { "TEMP" => @"C:\right", "TMPDIR" => @"C:\wrong", _ => null }, "m", isWindows: true);

        Assert.Equal("/right", linux.Temp);
        Assert.Equal(@"C:\right", windows.Temp);
        Assert.Null(new HostingEnvironmentInfo(n => n == "TEMP" ? "/wrong" : null, "m", isWindows: false).Temp);
    }

    [Fact]
    public void Nuget_cache_stays_on_the_shared_home_mount_even_when_home_points_elsewhere()
    {
        var catalog = Catalog(Build.LinuxAzure(("HOME", "/root")));

        var nuget = catalog.Find(StatsTargetKeys.NuGetCache)!;
        Assert.Equal(Path.Combine("/home", ".nuget"), nuget.Path);
        Assert.Equal(StatsScope.Shared, nuget.Scope);
    }

    [Fact]
    public void Test_mode_on_linux_compares_mounts_too()
    {
        var env = new HostingEnvironmentInfo(n => n == "TMPDIR" ? "/tmp" : null, "m", isWindows: false);
        var catalog = new StatsTargetCatalog(
            "/app", new CloudHealthChecksOptions { LocalTestMode = true }, "m", env, null, _ => true, p => p.StartsWith("/app") ? "/app" : "/");

        Assert.True(catalog.Find(StatsTargetKeys.TempDisk)!.AppliesToThisHost); // /tmp is on a different mount from the app

        var sameMount = new StatsTargetCatalog(
            "/app", new CloudHealthChecksOptions { LocalTestMode = true }, "m", env, null, _ => true, _ => "/");
        Assert.False(sameMount.Find(StatsTargetKeys.TempDisk)!.AppliesToThisHost);
    }

    [Fact]
    public void Symlinked_path_components_are_resolved_before_matching_mounts()
    {
        using var root = new TempFolder();
        var target = Directory.CreateDirectory(Path.Combine(root.Path, "target", "inner")).FullName;
        var link = Path.Combine(root.Path, "link");
        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(root.Path, "target"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return; // creating symlinks needs a privilege on Windows
        }

        // Both end up at the same real folder (the temp folder itself may sit behind a symlink on some systems)
        var resolved = StatsTargetCatalog.ResolveSymlinks(Path.Combine(link, "inner"));
        Assert.Equal(StatsTargetCatalog.ResolveSymlinks(target), resolved);
        Assert.DoesNotContain("link", resolved.Substring(StatsTargetCatalog.ResolveSymlinks(root.Path).Length));
    }

    [Fact]
    public void A_mount_matches_whole_directory_names_only()
    {
        var root = Path.GetFullPath("/");
        var home = Path.GetFullPath("/home");
        var mounts = new[] { root, home };

        Assert.Equal(home, StatsTargetCatalog.FindMountPoint("/home/tmp", mounts));
        Assert.Equal(home, StatsTargetCatalog.FindMountPoint("/home", mounts));
        Assert.Equal(root, StatsTargetCatalog.FindMountPoint("/home2/tmp", mounts)); // sibling prefix: not under /home
        Assert.Equal(root, StatsTargetCatalog.FindMountPoint("/tmp", mounts));
    }

    [Fact]
    public void Mount_point_is_found_for_a_real_folder()
    {
        using var root = new TempFolder();

        var mount = StatsTargetCatalog.FindMountPoint(root.Path);

        Assert.NotNull(mount);
        Assert.StartsWith(mount!, Path.GetFullPath(root.Path));
    }
}

public class DiskUsageReaderTests
{
    [Fact]
    public void Reads_total_and_free_space_for_an_existing_folder_on_this_os()
    {
        using var root = new TempFolder();

        var usage = new DiskUsageReader().Read(root.Path);

        Assert.NotNull(usage);
        var value = usage!.Value;
        Assert.True(value.TotalBytes > 0);
        Assert.InRange(value.FreeBytes, 0, value.TotalBytes);
    }

    [Fact]
    public void Missing_folder_or_blank_path_gives_null()
    {
        var reader = new DiskUsageReader();

        Assert.Null(reader.Read(Path.Combine(Path.GetTempPath(), "chc-missing-" + Guid.NewGuid().ToString("N"))));
        Assert.Null(reader.Read(" "));
    }
}

public class HostingPlatformTests
{
    private static IConfiguration Config(params (string, string?)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build();

    private static HostingPlatform Detect(IConfiguration c, HostingEnvironmentInfo env, bool cloudJson = false) =>
        HostingPlatformDetector.Detect(c, env, "/root", p => cloudJson && p.EndsWith("umbraco-cloud.json"));

    [Fact]
    public void Is_running_on_cloud_flag_means_umbraco_cloud()
    {
        Assert.Equal(HostingPlatform.UmbracoCloud, Detect(Config(("Umbraco:Cloud:IsRunningOnCloud", "true")), Build.OffAzure()));
    }

    [Fact]
    public void App_service_with_cloud_environment_id_or_json_is_umbraco_cloud()
    {
        Assert.Equal(HostingPlatform.UmbracoCloud, Detect(Config(("Umbraco:Cloud:Identity:EnvironmentId", "abc")), Build.Azure()));
        Assert.Equal(HostingPlatform.UmbracoCloud, Detect(Config(), Build.Azure(), cloudJson: true));
    }

    [Fact]
    public void Plain_app_service_is_an_azure_web_app()
    {
        Assert.Equal(HostingPlatform.AzureWebApp, Detect(Config(), Build.Azure()));
    }

    [Fact]
    public void A_local_clone_of_a_cloud_project_is_not_hosted_on_cloud()
    {
        Assert.Equal(HostingPlatform.Other, Detect(Config(("Umbraco:Cloud:Identity:EnvironmentId", "abc")), Build.OffAzure(), cloudJson: true));
    }

    [Fact]
    public void Group_names_follow_the_platform()
    {
        Assert.Equal("Umbraco Cloud", HostingPlatformDetector.GroupName(HostingPlatform.UmbracoCloud));
        Assert.Equal("Azure Web App", HostingPlatformDetector.GroupName(HostingPlatform.AzureWebApp));
        Assert.Equal("Hosting", HostingPlatformDetector.GroupName(HostingPlatform.Other));
    }

    [Fact]
    public void Other_hosts_only_monitor_the_logs_even_when_a_home_folder_exists()
    {
        using var root = new TempFolder();
        var catalog = new StatsTargetCatalog(root.Path, new CloudHealthChecksOptions(), "m", Build.OffAzure(), null, _ => true);

        Assert.Equal(new[] { StatsTargetKeys.UmbracoLogs }, catalog.Targets.Where(t => t.AppliesToThisHost).Select(t => t.Key));
    }

    [Fact]
    public void Umbraco_cloud_and_azure_web_apps_monitor_everything_that_exists()
    {
        using var root = new TempFolder();
        foreach (var platform in new[] { HostingPlatform.UmbracoCloud, HostingPlatform.AzureWebApp })
        {
            var catalog = new StatsTargetCatalog(root.Path, new CloudHealthChecksOptions(), "m", Build.Azure(), platform, _ => true);
            Assert.True(catalog.Find(StatsTargetKeys.NuGetCache)!.AppliesToThisHost);
            Assert.True(catalog.Find(StatsTargetKeys.LocalTemp)!.AppliesToThisHost);
            Assert.True(catalog.Find(StatsTargetKeys.HomeDisk)!.AppliesToThisHost);
            Assert.True(catalog.Find(StatsTargetKeys.LocalDisk)!.AppliesToThisHost);
        }
    }

    [Fact]
    public async Task Health_check_group_is_renamed_for_the_detected_platform()
    {
        using var root = new TempFolder();
        var store = new InMemoryResultStore();
        var options = Microsoft.Extensions.Options.Options.Create(new CloudHealthChecksOptions());
        var disk = new FakeDiskReader();

        foreach (var (platform, expected) in new[] { (HostingPlatform.UmbracoCloud, "Umbraco Cloud"), (HostingPlatform.AzureWebApp, "Azure Web App"), (HostingPlatform.Other, "Hosting") })
        {
            var catalog = new StatsTargetCatalog(root.Path, options.Value, "m", Build.OffAzure(), platform, _ => false);
            var check = new UmbracoLogsFolderSizeHealthCheck(
                store, new FakeHostEnvironment { ContentRootPath = root.Path }, options,
                Build.Service(store, catalog, disk, options.Value), new StatsThresholds(options, catalog, disk), catalog, ServersBuild.Servers(store, catalog));
            Assert.Equal(expected, check.Group);
        }

        await Task.CompletedTask;
    }
}
