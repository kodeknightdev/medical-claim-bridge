using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ClaimBridge.Gateway.Filters;

namespace ClaimBridge.Gateway.Composition;

public static class Sources
{
    public const string Claims = "claims";
    public const string Scrubber = "scrubber";
    public const string Payers = "payers-gateway";
    public const string Remittance = "remittance";
    public const string Risk = "denial-predictor";

    public static readonly string[] Optional = [Scrubber, Payers, Remittance, Risk];
}

public sealed record TimelineEntry(DateTimeOffset At, string Source, string Kind, string Text);

public sealed record ClaimDetailsResponse(
    JsonElement Claim,
    JsonElement? ScrubResults,
    JsonElement? PayerHistory,
    JsonElement? Remittance,
    JsonElement? Risk,
    IReadOnlyList<TimelineEntry> Timeline,
    bool Partial,
    IReadOnlyList<string> Unavailable);

public enum CompositionOutcome { Ok, NotFound, Failed }

public sealed record CompositionResult(CompositionOutcome Outcome, ClaimDetailsResponse? Response = null, string? Error = null);

/// <summary>
/// API composition for the claim detail page: claims is required, the other four sources are fetched in parallel
/// and may be missing, in which case the response says which ones and is flagged as partial.
/// </summary>
public class ClaimDetailsComposer(IHttpClientFactory clients, GatewayMetrics metrics, ILogger<ClaimDetailsComposer> logger)
{
    public static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(3);

    public async Task<CompositionResult> ComposeAsync(Guid claimId, IReadOnlySet<string> include, string? bearer, CancellationToken ct)
    {
        var claimTask = FetchAsync(Sources.Claims, $"api/claims/{claimId}", bearer, ct);
        var optional = Sources.Optional.Where(include.Contains).ToDictionary(s => s, s => FetchAsync(s, PathFor(s, claimId), bearer, ct));
        await Task.WhenAll([claimTask, .. optional.Values]);

        var claim = claimTask.Result;
        if (claim.Status == HttpStatusCode.NotFound)
            return new CompositionResult(CompositionOutcome.NotFound);
        if (claim.Body is null)
            return new CompositionResult(CompositionOutcome.Failed, Error: claim.Error ?? "claims service unavailable");

        var unavailable = optional.Where(kv => kv.Value.Result.Body is null && kv.Value.Result.Status != HttpStatusCode.NotFound)
            .Select(kv => kv.Key).ToList();
        foreach (var source in unavailable) metrics.Partial(source);

        JsonElement? Body(string source) => optional.TryGetValue(source, out var t) ? t.Result.Body : null;
        var response = new ClaimDetailsResponse(claim.Body.Value, Body(Sources.Scrubber), Body(Sources.Payers), Body(Sources.Remittance),
            Body(Sources.Risk), BuildTimeline(claim.Body.Value, Body(Sources.Payers), Body(Sources.Remittance)), unavailable.Count > 0,
            unavailable);
        return new CompositionResult(CompositionOutcome.Ok, response);
    }

    internal static string PathFor(string source, Guid claimId) => source switch
    {
        Sources.Scrubber => $"api/scrub-results/{claimId}",
        Sources.Payers => $"api/submissions/by-claim/{claimId}",
        Sources.Remittance => $"api/remittances/by-claim/{claimId}",
        Sources.Risk => $"api/risk/claims/{claimId}",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private sealed record Fetched(HttpStatusCode? Status, JsonElement? Body, string? Error);

    private async Task<Fetched> FetchAsync(string source, string path, string? bearer, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SourceTimeout);
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var response = await clients.CreateClient(source).SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return new Fetched(response.StatusCode, null, $"HTTP {(int)response.StatusCode}");
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
            return new Fetched(response.StatusCode, body, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Composition source {Source} failed after {Elapsed} ms: {Error}", source, watch.ElapsedMilliseconds, ex.Message);
            return new Fetched(null, null, ex.Message);
        }
    }

    internal static IReadOnlyList<TimelineEntry> BuildTimeline(JsonElement claim, JsonElement? payer, JsonElement? remittance)
    {
        var entries = new List<TimelineEntry>();
        if (claim.TryGetProperty("history", out var history))
            foreach (var h in history.EnumerateArray())
                entries.Add(new TimelineEntry(h.GetProperty("occurredAt").GetDateTimeOffset(), "claims", h.GetProperty("trigger").GetString()!,
                    $"{h.GetProperty("fromStatus").GetString()} → {h.GetProperty("toStatus").GetString()}" +
                    (h.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? $": {r.GetString()}" : "")));

        if (payer is { } p && p.TryGetProperty("history", out var payerHistory))
            foreach (var h in payerHistory.EnumerateArray())
                entries.Add(new TimelineEntry(h.GetProperty("at").GetDateTimeOffset(), "payer", h.GetProperty("kind").GetString()!,
                    $"{h.GetProperty("status").GetString()}" +
                    (h.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? $": {d.GetString()}" : "")));

        if (remittance is { ValueKind: JsonValueKind.Array } rem)
            foreach (var posting in rem.EnumerateArray())
                entries.Add(new TimelineEntry(posting.GetProperty("postedAt").GetDateTimeOffset(), "remittance", posting.GetProperty("kind").GetString()!,
                    $"Paid {posting.GetProperty("paid").GetDecimal():0.00}, patient {posting.GetProperty("patientResponsibility").GetDecimal():0.00}"));

        return entries.OrderBy(e => e.At).ToList();
    }
}
