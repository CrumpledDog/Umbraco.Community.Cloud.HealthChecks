namespace Umbraco.Community.Cloud.HealthChecks.Stats;

/// <summary>Azure App Service detection and the site key. Variables are read through a delegate so it can be tested.</summary>
internal sealed class HostingEnvironmentInfo
{
    public HostingEnvironmentInfo()
        : this(Environment.GetEnvironmentVariable, Environment.MachineName)
    {
    }

    public HostingEnvironmentInfo(Func<string, string?> getVariable, string machineName, bool? isWindows = null)
    {
        IsWindows = isWindows ?? OperatingSystem.IsWindows();

        var siteName = getVariable("WEBSITE_SITE_NAME");
        var instanceId = getVariable("WEBSITE_INSTANCE_ID");

        // Both must be set: either alone also appears in local tooling and containers.
        IsAppService = !string.IsNullOrWhiteSpace(siteName) && !string.IsNullOrWhiteSpace(instanceId);

        var slot = getVariable("WEBSITE_SLOT_NAME");
        SiteKey = IsAppService
            ? (string.IsNullOrWhiteSpace(slot) ? siteName! : $"{siteName}:{slot}")
            : machineName;

        // TEMP on Windows, TMPDIR on Linux and macOS
        Temp = NullIfBlank(getVariable("TEMP")) ?? NullIfBlank(getVariable("TMPDIR"));
        Home = NullIfBlank(getVariable("HOME"));
    }

    /// <summary>True when running on Windows (App Service on Windows); false for Linux and macOS.</summary>
    public bool IsWindows { get; }

    /// <summary>True on Azure App Service (Windows or Linux).</summary>
    public bool IsAppService { get; }

    /// <summary>Identifies the disk shared by every instance of a web app.</summary>
    public string SiteKey { get; }

    /// <summary>The TEMP (or, on Linux, TMPDIR) environment variable, if set.</summary>
    public string? Temp { get; }

    /// <summary>The HOME environment variable, if set (<c>/home</c> on Linux App Service).</summary>
    public string? Home { get; }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
