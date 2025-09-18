using ClaimBridge.Claims.Application;
using ClaimBridge.Claims.Domain;

namespace ClaimBridge.Claims.Api;

public sealed record ClaimSummaryDto(
    Guid Id, string ClaimNumber, Guid OrgId, string PayerId, string Status, string FrequencyCode, int Version,
    string MemberIdMasked, decimal TotalCharge, decimal? PaidAmount, DateOnly? FirstServiceDate,
    DateTimeOffset CreatedAt, DateTimeOffset StatusChangedAt, decimal? DenialRisk, string Source);

public sealed record ServiceLineDto(int LineNumber, string ProcedureCode, IReadOnlyList<string> Modifiers, decimal Units,
    decimal Charge, DateOnly ServiceDate, string PlaceOfService, IReadOnlyList<int> DxPointers);

public sealed record HistoryDto(int Sequence, string FromStatus, string ToStatus, string Trigger, string? Reason, string Source,
    DateTimeOffset OccurredAt);

public sealed record ClaimDetailDto(
    ClaimSummaryDto Summary,
    PatientPhi Patient,
    string BillingNpi,
    string? RenderingNpi,
    string? PriorAuthNumber,
    string? OriginalClaimNumber,
    Guid? PreviousClaimId,
    Guid? SupersededByClaimId,
    IReadOnlyList<string> Diagnoses,
    IReadOnlyList<ServiceLineDto> Lines,
    IReadOnlyList<HistoryDto> History,
    string? PayerClaimId,
    decimal? AllowedAmount,
    decimal? PatientResponsibility,
    decimal? AdjustmentAmount,
    string? DenialCategory,
    string? DenialCarc,
    string? PossibleDuplicateOf,
    object? DenialRiskFactors);

public static class ClaimMapping
{
    public static ClaimSummaryDto ToSummary(this Claim c) => new(
        c.Id, c.ClaimNumber, c.OrgId, c.PayerId, c.Status.ToString(), c.FrequencyCode, c.Version, "****" + c.MemberIdLast4,
        c.TotalCharge, c.PaidAmount, c.Lines.Count == 0 ? null : c.FirstServiceDate, c.CreatedAt, c.StatusChangedAt,
        c.DenialRisk, c.Source.ToString());

    public static HistoryDto ToDto(this ClaimStatusEvent h) =>
        new(h.Sequence, h.FromStatus.ToString(), h.ToStatus.ToString(), h.Trigger, h.Reason, h.Source, h.OccurredAt);
}
