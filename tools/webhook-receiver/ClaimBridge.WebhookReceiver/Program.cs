using System.Collections.Concurrent;
using ClaimBridge.Contracts.Webhooks;

// Sample provider-side receiver: verifies the claim-bridge signature before trusting a delivery.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var received = new ConcurrentQueue<object>();
var secrets = app.Configuration.GetSection("Receiver:Secrets").Get<Dictionary<string, string>>() ?? [];

app.MapPost("/webhooks/{tenant}", async (string tenant, HttpRequest request, ILogger<Program> logger) =>
{
    // read the raw body: re-serialising the JSON would change bytes and break the signature
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    var header = request.Headers[WebhookSignature.HeaderName].ToString();

    if (!secrets.TryGetValue(tenant, out var secret))
        return Results.NotFound();

    var valid = WebhookSignature.Verify(secret, header, body, DateTimeOffset.UtcNow);
    var entry = new
    {
        tenant,
        at = DateTimeOffset.UtcNow,
        eventType = request.Headers[WebhookSignature.EventHeader].ToString(),
        delivery = request.Headers[WebhookSignature.DeliveryHeader].ToString(),
        signatureValid = valid,
        body = System.Text.Json.JsonDocument.Parse(body).RootElement
    };
    received.Enqueue(entry);
    while (received.Count > 500) received.TryDequeue(out _);

    logger.LogInformation("{Event} for {Tenant}: signature {Result}", entry.eventType, tenant, valid ? "valid" : "INVALID");
    return valid ? Results.Ok(new { received = true }) : Results.Unauthorized();
});

app.MapGet("/webhooks", () => received.Reverse());
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/", () => Results.Text("""
    claim-bridge sample webhook receiver
    POST /webhooks/{tenant}  verifies X-ClaimBridge-Signature (t=...,v1=hex hmac-sha256 of "t.body")
    GET  /webhooks           last 500 deliveries with the verification result
    """));

app.Run();

public partial class Program;
