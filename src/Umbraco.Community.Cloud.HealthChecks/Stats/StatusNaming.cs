using Umbraco.Cms.Core.HealthChecks;

namespace Umbraco.Community.Cloud.HealthChecks.Stats;

internal static class StatusNaming
{
    /// <summary>HealthCheckStatus.Message is read only, so a named copy is made.</summary>
    public static HealthCheckStatus WithServerName(HealthCheckStatus status, string serverName)
    {
        var named = new HealthCheckStatus($"[{serverName}] {status.Message}")
        {
            ResultType = status.ResultType,
            Description = status.Description,
            ReadMoreLink = status.ReadMoreLink
        };

        if (status.Actions != null)
        {
            named.Actions = status.Actions;
        }

        return named;
    }

    public static List<HealthCheckStatus> WithServerName(IEnumerable<HealthCheckStatus> statuses, string serverName) =>
        statuses.Select(s => WithServerName(s, serverName)).ToList();
}
