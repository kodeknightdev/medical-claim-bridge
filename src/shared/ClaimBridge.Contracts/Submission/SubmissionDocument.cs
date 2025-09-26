namespace ClaimBridge.Contracts.Submission;

/// <summary>
/// Payer-neutral claim document that payers-gateway fetches over the internal API (claim check). It is the only
/// place PHI leaves the claims service, and it never goes onto Kafka.
/// </summary>
public sealed record SubmissionDocument(
    Guid ClaimId,
    string ClaimNumber,
    int Version,
    string FrequencyCode,
    string? OriginalClaimNumber,
    string? OriginalPayerClaimId,
    Guid OrgId,
    string PayerId,
    BillingProvider Billing,
    string? RenderingNpi,
    SubscriberInfo Subscriber,
    string? PriorAuthNumber,
    IReadOnlyList<string> Diagnoses,
    IReadOnlyList<SubmissionLine> Lines,
    decimal TotalCharge);

public sealed record BillingProvider(string Name, string Npi, string TaxId, string AddressLine1, string City, string State, string Zip);

public sealed record SubscriberInfo(string MemberId, string FirstName, string LastName, DateOnly DateOfBirth, string Gender);

public sealed record SubmissionLine(
    int LineNumber,
    string ProcedureCode,
    IReadOnlyList<string> Modifiers,
    decimal Units,
    decimal Charge,
    DateOnly ServiceDate,
    string PlaceOfService,
    IReadOnlyList<int> DxPointers);
