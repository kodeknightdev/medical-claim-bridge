using System.IO.Compression;
using System.Text.Json;
using ClaimBridge.Contracts.ReferenceData;
using ClaimBridge.Remittance.Data;
using ClaimBridge.Remittance.Domain;
using ClaimBridge.ServiceDefaults.Storage;
using ClaimBridge.X12.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Remittance.Seeding;

/// <summary>Builds expected claims, A/R balances and postings from the seeder's synthetic history.</summary>
public class HistoryImporter(RemittanceDb db, IBlobStore blobs, ILogger<HistoryImporter> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Line(DateOnly ServiceDate);
    private sealed record Adj(string Group, string Reason, decimal Amount);
    private sealed record Record(Guid ClaimId, string ClaimNumber, Guid OrgId, string PayerId, decimal TotalCharge, List<Line> Lines,
        DateTimeOffset SubmittedAt, string? Outcome, string? Carc, decimal PaidAmount, decimal AllowedAmount, decimal PatientResponsibility,
        List<Adj> Adjustments, DateTimeOffset? AdjudicatedAt, string? EftTraceNumber, bool? DeniedInitially, string? FinalStatus);

    public async Task<int> ImportAsync(BlobRef blob, CancellationToken ct)
    {
        var records = await ReadAsync(blob, ct);
        var known = (await db.ExpectedClaims.Select(c => c.ClaimNumber).ToListAsync(ct)).ToHashSet();
        var fresh = records.Where(r => !known.Contains(r.ClaimNumber)).ToList();

        foreach (var r in fresh)
        {
            db.ExpectedClaims.Add(new ExpectedClaim
            {
                ClaimId = r.ClaimId, ClaimNumber = r.ClaimNumber, OrgId = r.OrgId, PayerId = r.PayerId, TotalCharge = r.TotalCharge,
                LastStatus = r.FinalStatus ?? "AcceptedByPayer", SubmittedAt = r.SubmittedAt, FirstServiceDate = r.Lines.Min(l => l.ServiceDate),
                UpdatedAt = r.AdjudicatedAt ?? r.SubmittedAt
            });
            var balance = new ClaimBalance
            {
                ClaimId = r.ClaimId, ClaimNumber = r.ClaimNumber, OrgId = r.OrgId, PayerId = r.PayerId, Charge = r.TotalCharge,
                Status = BalanceStatus.Open, OpenedAt = r.SubmittedAt, FirstServiceDate = r.Lines.Min(l => l.ServiceDate),
                UpdatedAt = r.AdjudicatedAt ?? r.SubmittedAt
            };
            balance.Recalculate();
            db.Balances.Add(balance);
        }

        // one 835 per payer per payment day, like a weekly-cycle payer would send
        foreach (var file in fresh.Where(r => r.AdjudicatedAt is not null).GroupBy(r => (r.PayerId, Day: DateOnly.FromDateTime(r.AdjudicatedAt!.Value.UtcDateTime))))
        {
            var remittance = new RemittanceFile
            {
                Id = Guid.NewGuid(), PayerId = file.Key.PayerId, BlobContainer = "seed", BlobName = "history.jsonl.gz",
                EftTraceNumber = file.First().EftTraceNumber, PaymentDate = file.Key.Day, PaymentMethod = "ACH",
                ClaimCount = file.Count(), MatchedCount = file.Count(), Status = "Posted", ReceivedAt = file.First().AdjudicatedAt!.Value
            };
            db.Files.Add(remittance);

            foreach (var r in file)
            {
                var clp = new ClaimPayment(r.ClaimNumber, r.DeniedInitially == true ? "4" : "1", r.TotalCharge,
                    r.DeniedInitially == true ? 0 : r.PaidAmount, r.DeniedInitially == true ? 0 : r.PatientResponsibility, null,
                    r.Adjustments.Select(a => new Adjustment(a.Group, a.Reason, a.Amount)).ToList(), [], []);
                var result = PostingCalculator.Calculate(clp);
                remittance.TotalPaid += result.Paid;
                db.Postings.Add(new Posting
                {
                    Id = Guid.NewGuid(), RemittanceFileId = remittance.Id, ClaimId = r.ClaimId, ClaimNumber = r.ClaimNumber, OrgId = r.OrgId,
                    PayerId = r.PayerId, Kind = result.Kind.ToString(), ClpStatus = clp.StatusCode, Charge = result.Charge, Paid = result.Paid,
                    Allowed = result.Allowed, PatientResponsibility = result.PatientResponsibility,
                    ContractualAdjustments = result.ContractualAdjustments, OtherAdjustments = result.OtherAdjustments,
                    DenialCarc = result.PrimaryDenialCarc, DenialCategory = result.PrimaryDenialCarc is { } c ? AdjustmentReasons.CategoryFor(c) : null,
                    EftTraceNumber = r.EftTraceNumber, PaymentDate = file.Key.Day, PostedAt = r.AdjudicatedAt!.Value,
                    Adjustments = result.Adjustments.Select(a => new PostingAdjustment { GroupCode = a.GroupCode, ReasonCode = a.ReasonCode, Amount = a.Amount }).ToList()
                });
                var balance = db.Balances.Local.First(b => b.ClaimId == r.ClaimId);
                PostingService.ApplyToBalance(balance, result, r.AdjudicatedAt!.Value);
                if (r.FinalStatus == "Closed")
                {
                    balance.Status = BalanceStatus.Closed;
                    balance.Recalculate();
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Imported {Count} historical claims into remittance", fresh.Count);
        return fresh.Count;
    }

    private async Task<List<Record>> ReadAsync(BlobRef blob, CancellationToken ct)
    {
        await using var raw = await blobs.OpenReadAsync(blob, ct);
        Stream stream = blob.Name.EndsWith(".gz") ? new GZipStream(raw, CompressionMode.Decompress) : raw;
        using var reader = new StreamReader(stream);
        var list = new List<Record>();
        while (await reader.ReadLineAsync(ct) is { } line)
            if (line.Length > 0) list.Add(JsonSerializer.Deserialize<Record>(line, Json)!);
        return list;
    }
}
