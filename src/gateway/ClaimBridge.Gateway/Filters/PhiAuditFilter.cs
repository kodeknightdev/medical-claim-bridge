using System.Diagnostics.Metrics;
using ClaimBridge.ServiceDefaults.Auth;

namespace ClaimBridge.Gateway.Filters;

/// <summary>Writes an audit record for every successful read of a PHI-bearing endpoint. The record holds ids, never PHI.</summary>
public sealed class PhiAuditFilter(ILoggerFactory loggers, GatewayMetrics metrics) : IEndpointFilter
{
    private readonly ILogger _audit = loggers.CreateLogger("Audit");

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var http = context.HttpContext;
        if (http.Response.StatusCode < 400)
        {
            var resource = http.GetRouteValue("id")?.ToString() ?? http.Request.Path.Value ?? "";
            _audit.LogInformation("PHI read {Endpoint} {Resource} by {Actor} org {OrgId} client {ClientId}",
                http.GetEndpoint()?.DisplayName, resource, http.User.Actor(), http.User.OrgId(), http.User.ClientId());
            metrics.PhiRead(http.GetEndpoint()?.DisplayName ?? "unknown");
        }
        return result;
    }
}

public sealed class GatewayMetrics
{
    private readonly Counter<long> _phiReads;
    private readonly Counter<long> _partialCompositions;

    public GatewayMetrics(IMeterFactory meters)
    {
        var meter = meters.Create("ClaimBridge.Gateway");
        _phiReads = meter.CreateCounter<long>("gateway.phi.reads", description: "Audited PHI-bearing reads");
        _partialCompositions = meter.CreateCounter<long>("gateway.composition.partial", description: "Claim detail responses missing a source");
    }

    public void PhiRead(string endpoint) => _phiReads.Add(1, new KeyValuePair<string, object?>("endpoint", endpoint));

    public void Partial(string source) => _partialCompositions.Add(1, new KeyValuePair<string, object?>("source", source));
}
