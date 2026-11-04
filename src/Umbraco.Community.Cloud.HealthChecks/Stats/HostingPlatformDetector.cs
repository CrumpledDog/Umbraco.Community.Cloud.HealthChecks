using System.Reflection;
using Microsoft.Extensions.Configuration;
using Umbraco.Cms.Core.HealthChecks;

namespace Umbraco.Community.Cloud.HealthChecks.Stats;

/// <summary>Detects Umbraco Cloud / Azure Web Apps and names the health check group to match.</summary>
internal static class HostingPlatformDetector
{
    public const string UmbracoCloudGroup = "Umbraco Cloud";
    public const string AzureWebAppGroup = "Azure Web App";
    public const string OtherGroup = "Hosting";

    /// <summary>
    /// Umbraco Cloud is <c>Umbraco:Cloud:IsRunningOnCloud</c>, or an App Service that has the Umbraco Cloud environment id or an
    /// <c>umbraco-cloud.json</c> (the Umbraco Cloud package registers its config via a composer, so the file is a fallback).
    /// A local clone of a Cloud project is not on App Service, so it is not reported as hosted on Umbraco Cloud.
    /// </summary>
    public static HostingPlatform Detect(IConfiguration configuration, HostingEnvironmentInfo env, string contentRoot, Func<string, bool> fileExists)
    {
        if (configuration.GetValue<bool>("Umbraco:Cloud:IsRunningOnCloud"))
        {
            return HostingPlatform.UmbracoCloud;
        }

        if (!env.IsAppService)
        {
            return HostingPlatform.Other;
        }

        var hasCloudSignal =
            !string.IsNullOrEmpty(configuration.GetValue<string>("Umbraco:Cloud:Identity:EnvironmentId"))
            || fileExists(Path.Combine(contentRoot, "umbraco-cloud.json"));

        return hasCloudSignal ? HostingPlatform.UmbracoCloud : HostingPlatform.AzureWebApp;
    }

    public static string GroupName(HostingPlatform platform) => platform switch
    {
        HostingPlatform.UmbracoCloud => UmbracoCloudGroup,
        HostingPlatform.AzureWebApp => AzureWebAppGroup,
        _ => OtherGroup
    };

    /// <summary>
    /// <c>HealthCheck.Group</c> is filled from the static <c>[HealthCheck]</c> attribute and is not virtual, so the per-host name
    /// is set through its private setter. If a future Umbraco changes that, the attribute's group is kept.
    /// </summary>
    public static void ApplyGroup(HealthCheck check, HostingPlatform platform)
    {
        try
        {
            typeof(HealthCheck).GetProperty(nameof(HealthCheck.Group))?
                .GetSetMethod(nonPublic: true)?
                .Invoke(check, new object[] { GroupName(platform) });
        }
        catch
        {
            // keep the attribute's group
        }
    }
}
