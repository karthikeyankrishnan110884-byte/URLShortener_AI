using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UrlShortener.Api.Services.Analytics;

/// <summary>Readiness: report Degraded when the click queue is nearly full, so a load balancer can shed traffic.</summary>
public sealed class ClickQueueHealthCheck(ChannelClickQueue queue) : IHealthCheck
{
    internal const double DegradedThreshold = 0.9;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["pending"] = queue.Pending,
            ["capacity"] = queue.Capacity,
            ["enqueued"] = queue.Enqueued,
            ["dropped"] = queue.Dropped,
        };

        var fill = (double)queue.Pending / queue.Capacity;
        return Task.FromResult(fill >= DegradedThreshold
            ? HealthCheckResult.Degraded($"Click queue {fill:P0} full.", data: data)
            : HealthCheckResult.Healthy("Click queue OK.", data));
    }
}
