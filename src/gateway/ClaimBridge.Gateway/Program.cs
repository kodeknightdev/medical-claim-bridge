using ClaimBridge.Gateway.Composition;
using ClaimBridge.Gateway.Endpoints;
using ClaimBridge.Gateway.Filters;
using ClaimBridge.Gateway.RateLimiting;
using ClaimBridge.ServiceDefaults;
using ClaimBridge.ServiceDefaults.Auth;
using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("gateway");
builder.AddClaimBridgeAuth();

var config = builder.Configuration;
builder.Services.AddReverseProxy().LoadFromConfig(config.GetSection("ReverseProxy"));
builder.Services.AddPerClientRateLimiting(config);
builder.Services.AddValidatorsFromAssemblyContaining<ClaimDetailsRequestValidator>();
builder.Services.AddSingleton<GatewayMetrics>();
builder.Services.AddScoped<ClaimDetailsComposer>();

// composition reuses the YARP cluster addresses so there's one place to point at a service
foreach (var source in new[] { Sources.Claims, Sources.Scrubber, Sources.Payers, Sources.Remittance, Sources.Risk })
{
    var address = config[$"ReverseProxy:Clusters:{source}:Destinations:primary:Address"]
                  ?? throw new InvalidOperationException($"No cluster address for {source}");
    builder.Services.AddHttpClient(source, c => c.BaseAddress = new Uri(address));
}

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Content-Disposition", "Retry-After", "Idempotent-Replayed")));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseCors();
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGatewayEndpoints();
app.MapReverseProxy();

app.Run();

public partial class Program;
