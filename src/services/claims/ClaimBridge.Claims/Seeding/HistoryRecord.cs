using System.IO.Compression;
using System.Text.Json;
using ClaimBridge.ServiceDefaults.Storage;

namespace ClaimBridge.Claims.Seeding;

/// <summary>Shape of one line in the seeder's history.jsonl.gz (see tools/seed).</summary>
public sealed record HistoryRecord(
    Guid ClaimId,
    string ClaimNumber,
    Guid OrgId,
    string PayerId,
    string BillingNpi,
    string? RenderingNpi,
    string MemberId,
    HistoryPatient Patient,
    string? PriorAuthNumber,
    string FrequencyCode,
    List<string> Diagnoses,
    List<HistoryLine> Lines,
    decimal TotalCharge,
    DateTimeOffset CreatedAt,
    DateTimeOffset SubmittedAt,
    HistoryScrub Scrub,
    bool AcceptedFirstPass,
    string? Outcome,
    string? Carc,
    string? Category,
    decimal PaidAmount,
    decimal AllowedAmount,
    decimal PatientResponsibility,
    List<HistoryAdjustment> Adjustments,
    DateTimeOffset? AdjudicatedAt,
    string? EftTraceNumber,
    HistoryAppeal? Appeal,
    bool? DeniedInitially);

public sealed record HistoryPatient(string FirstName, string LastName, DateOnly DateOfBirth, string Gender);
public sealed record HistoryLine(string ProcedureCode, List<string> Modifiers, decimal Units, decimal Charge, DateOnly ServiceDate,
    string PlaceOfService, List<int> DxPointers);
public sealed record HistoryScrub(bool FirstPassClean, List<HistoryEdit> Edits);
public sealed record HistoryEdit(string RuleCode, string Severity);
public sealed record HistoryAdjustment(string Group, string Reason, decimal Amount);
public sealed record HistoryAppeal(string Status, DateTimeOffset SubmittedAt);

public static class HistoryReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<List<HistoryRecord>> ReadAsync(IBlobStore blobs, BlobRef blob, CancellationToken ct)
    {
        await using var raw = await blobs.OpenReadAsync(blob, ct);
        Stream stream = blob.Name.EndsWith(".gz") ? new GZipStream(raw, CompressionMode.Decompress) : raw;
        using var reader = new StreamReader(stream);
        var records = new List<HistoryRecord>();
        while (await reader.ReadLineAsync(ct) is { } line)
            if (line.Length > 0)
                records.Add(JsonSerializer.Deserialize<HistoryRecord>(line, Json)!);
        return records;
    }
}
