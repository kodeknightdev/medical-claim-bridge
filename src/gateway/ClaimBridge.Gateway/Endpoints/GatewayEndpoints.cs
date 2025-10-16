using ClaimBridge.Gateway.Filters;
using ClaimBridge.Gateway.RateLimiting;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Yarp.ReverseProxy.Configuration;

namespace ClaimBridge.Gateway.Endpoints;

public static class GatewayEndpoints
{
    public sealed record MeResponse(string? Subject, string? Name, Guid? OrgId, string? ClientId, IReadOnlyList<string> Roles,
        IReadOnlyList<string> Scopes, bool IsStaff);

    public sealed record RouteInfo(string RouteId, string? Path, string ClusterId, string? AuthorizationPolicy);

    /// <summary>The gateway's own endpoints. Everything else under /api is proxied by YARP.</summary>
    public static WebApplication MapGatewayEndpoints(this WebApplication app)
    {
        var claims = app.MapGroup("/api/claims")
            .RequireAuthorization(Policies.ClaimsRead)
            .RequireRateLimiting(PerClientRateLimit.PolicyName)
            .WithTags("Composition");

        claims.MapGet("/{id:guid}/details", ClaimDetailsEndpoint.Handle)
            .AddEndpointFilter<ValidationFilter<ClaimDetailsRequest>>()
            .AddEndpointFilter<PhiAuditFilter>()
            .WithName("GetClaimDetails")
            .WithSummary("Claim with scrub results, payer acks and status history, remittance postings and the denial risk score");

        var gateway = app.MapGroup("/api/gateway").RequireAuthorization().WithTags("Gateway");
        gateway.MapGet("/me", Me).WithName("WhoAmI");
        gateway.MapGet("/routes", Routes).RequireAuthorization(Policies.Staff).WithName("ProxyRoutes");
        return app;
    }

    public static Ok<MeResponse> Me(HttpContext http)
    {
        var user = http.User;
        return TypedResults.Ok(new MeResponse(user.FindFirst("sub")?.Value, user.Identity?.Name, user.OrgId(), user.ClientId(),
            user.FindAll("role").Select(c => c.Value).ToList(),
            user.FindAll("scope").SelectMany(c => c.Value.Split(' ')).ToList(), user.IsStaff()));
    }

    public static Ok<List<RouteInfo>> Routes(IProxyConfigProvider proxy) =>
        TypedResults.Ok(proxy.GetConfig().Routes
            .Select(r => new RouteInfo(r.RouteId, r.Match.Path, r.ClusterId ?? "", r.AuthorizationPolicy))
            .OrderBy(r => r.Path).ToList());
}
