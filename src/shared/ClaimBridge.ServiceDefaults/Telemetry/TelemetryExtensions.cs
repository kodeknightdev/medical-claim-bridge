using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ClaimBridge.ServiceDefaults;

public static class TelemetryExtensions
{
    /// <summary>
    /// Traces (ASP.NET Core, HttpClient incl. gRPC, Kafka produce/consume spans) and metrics (runtime + ClaimBridge.* meters),
    /// exported over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set. Span names and tags never carry PHI.
    /// </summary>
    public static WebApplicationBuilder AddClaimBridgeTelemetry(this WebApplicationBuilder builder, string serviceName)
    {
        var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var exportMetrics = builder.Configuration.GetValue("Telemetry:ExportMetrics", false);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceNamespace: "claim-bridge")
                .AddAttributes([new("deployment.environment", builder.Environment.EnvironmentName)]))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o =>
                {
                    o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health") && !ctx.Request.Path.StartsWithSegments("/alive");
                    // the raw query string can hold member ids on search endpoints
                    o.EnrichWithHttpRequest = (activity, _) => activity.SetTag("url.query", null);
                })
                .AddHttpClientInstrumentation()
                .AddSource(MessagingTelemetry.SourceName)
                .AddSource("Yarp.ReverseProxy");
                if (!string.IsNullOrWhiteSpace(otlp)) t.AddOtlpExporter();
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter("System.Runtime")
                    .AddMeter("ClaimBridge.*");
                // Jaeger only takes traces; point this at a collector to keep metrics
                if (!string.IsNullOrWhiteSpace(otlp) && exportMetrics) m.AddOtlpExporter();
            });
        return builder;
    }
}
