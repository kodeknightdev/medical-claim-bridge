using Azure.Storage.Blobs;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.PayerSim.Engine;
using ClaimBridge.X12.Core;
using ClaimBridge.X12.Transactions;

namespace ClaimBridge.PayerSim.Payers;

/// <summary>Bluepeak Mutual: picks up 837 files, answers with 999 and 277CA files, and later an 835.</summary>
public sealed class PayerBFileProcessor(
    BlobServiceClient blobs,
    ClaimStore store,
    MemberDirectory members,
    AdjudicationFlow flow,
    RemittanceWriter remits,
    SimScheduler scheduler,
    SimScenario scenario,
    ILogger<PayerBFileProcessor> logger) : BackgroundService
{
    private int _control = 7000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var container = blobs.GetBlobContainerClient("payer-b");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await container.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
                await foreach (var item in container.GetBlobsAsync(prefix: "inbound/", cancellationToken: stoppingToken))
                {
                    if (item.Name.Count(c => c == '/') != 1) continue;
                    var blob = container.GetBlobClient(item.Name);
                    var text = (await blob.DownloadContentAsync(stoppingToken)).Value.Content.ToString();
                    await container.GetBlobClient(item.Name.Replace("inbound/", "inbound/processed/"))
                        .UploadAsync(BinaryData.FromString(text), overwrite: true, stoppingToken);
                    await blob.DeleteIfExistsAsync(cancellationToken: stoppingToken);
                    await ProcessAsync(container, text, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Payer B inbound scan failed: {Error}", ex.Message);
            }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task ProcessAsync(BlobContainerClient container, string text, CancellationToken ct)
    {
        var interchange = X12Reader.Parse(text);
        var groupControl = interchange.Segments.First(s => s.Id == "GS")[6];
        var claims = Claim837PReader.Read(text);

        var rejectFile = scenario.Reject999Next;
        scenario.Reject999Next = false;
        var ack999 = new Ack999(groupControl, !rejectFile,
            [new SetAcknowledgement("0001", !rejectFile, rejectFile ? ["Simulated envelope error"] : [])]);

        var acks = new List<ClaimAcknowledgement>();
        var accepted = new List<SimClaimRecord>();
        foreach (var c in claims)
        {
            var sim = new SimClaim(SeedIds.PayerB, c.ClaimNumber, c.FrequencyCode, c.OriginalReference, c.Subscriber.MemberId,
                c.Subscriber.DateOfBirth, c.BillingNpi, c.BillingName, c.PriorAuthNumber, c.Diagnoses,
                c.Lines.Select(l => new SimLine(l.LineNumber, l.ProcedureCode, l.Modifiers, l.Units, l.Charge, l.ServiceDate, l.PlaceOfService)).ToList());
            if (members.Find(sim.MemberId) is null)
            {
                acks.Add(new ClaimAcknowledgement(c.ClaimNumber, false, "A7", "562", null, c.TotalCharge, "Subscriber not found"));
                continue;
            }
            var record = store.Add(sim, AdjudicationFlow.Prefix(SeedIds.PayerB));
            accepted.Add(record);
            acks.Add(new ClaimAcknowledgement(c.ClaimNumber, true, "A1", "20", record.PayerClaimId, c.TotalCharge, null));
        }

        var now = DateTimeOffset.UtcNow;
        var file999 = ack999.Write(new InterchangeOptions("BLUEPEAK", "CLAIMBRIDGE", Interlocked.Increment(ref _control), now));
        var file277 = rejectFile ? null : new ClaimAck277CA(acks)
            .Write(new InterchangeOptions("BLUEPEAK", "CLAIMBRIDGE", Interlocked.Increment(ref _control), now), "Bluepeak Mutual", "Claim Bridge");

        // like the real payer, the 277CA sometimes lands before the 999
        var flip = int.TryParse(groupControl, out var g) && g % 2 == 0;
        var delay = TimeSpan.FromSeconds(scenario.PayerBAckDelaySeconds);
        scheduler.Schedule(flip ? delay * 2 : delay, ct2 => Upload(container, $"outbound/BPK999_{groupControl}.x12", file999, ct2));
        if (file277 is not null)
            scheduler.Schedule(flip ? delay : delay * 2, ct2 => Upload(container, $"outbound/BPK277CA_{groupControl}.x12", file277, ct2));

        if (rejectFile || accepted.Count == 0) return;
        scheduler.Schedule(TimeSpan.FromSeconds(scenario.AdjudicationDelaySeconds) + delay * 2, async ct2 =>
        {
            foreach (var byPayee in accepted.GroupBy(r => (r.Claim.BillingNpi, r.Claim.BillingName)))
            {
                var payments = byPayee.SelectMany(flow.Adjudicate).ToList();
                await remits.WriteAsync(SeedIds.PayerB, byPayee.Key.BillingNpi, byPayee.Key.BillingName, payments, ct2);
            }
        });
        logger.LogInformation("Payer B took file {Group} with {Count} claim(s)", groupControl, claims.Count);
    }

    private static Task Upload(BlobContainerClient container, string name, string text, CancellationToken ct) =>
        container.GetBlobClient(name).UploadAsync(BinaryData.FromString(text), overwrite: true, ct);
}

public static class PayerBEndpoints
{
    public sealed record BpEligibilityRequest(string SubscriberId, string BirthDate, string ServiceDate);
    public sealed record BpStatusRequest(string PatientControlNumber, string? PayerClaimNumber);

    public static void MapPayerB(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/payer-b").WithTags("Payer B (Bluepeak)");

        group.MapPost("/eligibility", (BpEligibilityRequest req, MemberDirectory members) =>
        {
            var m = members.Find(req.SubscriberId);
            var dos = DateOnly.ParseExact(req.ServiceDate, "yyyyMMdd");
            if (m is null || m.PayerId != SeedIds.PayerB || m.DateOfBirth.ToString("yyyyMMdd") != req.BirthDate)
                return Results.Ok(new { eligibilityCode = "6" });
            return Results.Ok(new
            {
                eligibilityCode = members.IsCovered(m, dos) ? "1" : "6", planDescription = m.Plan,
                effectiveDate = m.CoverageStart.ToString("yyyyMMdd"), termDate = m.CoverageEnd?.ToString("yyyyMMdd"),
                officeCopay = m.Copay, deductibleLeft = Math.Max(0, m.Deductible - m.DeductibleMet)
            });
        });

        group.MapPost("/claim-status", (BpStatusRequest req, ClaimStore store) =>
        {
            var r = store.Find(SeedIds.PayerB, req.PatientControlNumber, req.PayerClaimNumber);
            if (r is null) return Results.Ok(new { statusCategory = "A4", message = "Claim not found" });
            var category = r.State switch
            {
                SimClaimState.Received or SimClaimState.InReview => "A2",
                SimClaimState.Pended => "P1",
                SimClaimState.Rejected => "A3",
                _ => r.Outcome switch { "denied" => "F2", "partial" => "F3", _ => "F1" }
            };
            return Results.Ok(new { statusCategory = category, statusCode = "20", payerClaimNumber = r.PayerClaimId, message = r.Note });
        });
    }
}
