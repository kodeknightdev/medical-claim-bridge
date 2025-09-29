using System.Text.Json.Serialization;

namespace ClaimBridge.PayersGateway.Adapters.PayerA;

// Northwind Health Plan's own JSON vocabulary. These types stay inside the adapter.

internal sealed record NwMember(string Id, string First, string Last, string Dob, string Sex);

internal sealed record NwProvider(string Npi, string TaxId, string Name);

internal sealed record NwService(string Code, string[] Mods, decimal Qty, decimal Amount, string Dos, string Pos, int[] DxRefs);

internal sealed record NwClaim(
    string SubmitterId,
    string ClaimRef,
    string Frequency,
    string? OriginalTrackingId,
    NwMember Member,
    NwProvider Billing,
    string? RenderingNpi,
    string? AuthNumber,
    string[] Dx,
    NwService[] Services,
    decimal Total,
    string CallbackUrl);

internal sealed record NwError(string Code, string Text);

internal sealed record NwSubmitResponse(string Status, string? TrackingId, NwError[]? Errors);

internal sealed record NwEligibilityRequest(string MemberId, string Dob, string Dos);

internal sealed record NwEligibilityResponse(bool Active, string? Plan, string? CoverageStart, string? CoverageEnd,
    decimal? Copay, decimal? DeductibleRemaining);

internal sealed record NwStatusRequest(string ClaimRef, string? TrackingId);

internal sealed record NwStatusResponse(string Status, string? Outcome, string? Note, string? TrackingId);

public sealed record NwCallback(
    [property: JsonPropertyName("trackingId")] string TrackingId,
    [property: JsonPropertyName("claimRef")] string ClaimRef,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("note")] string? Note);
