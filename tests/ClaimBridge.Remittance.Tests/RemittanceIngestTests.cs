using ClaimBridge.Contracts.Events;
using ClaimBridge.Remittance.Consumers;
using ClaimBridge.Remittance.Data;
using ClaimBridge.Remittance.Domain;
using ClaimBridge.ServiceDefaults.Storage;
using ClaimBridge.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ClaimBridge.Remittance.Tests;

[CollectionDefinition("sql")]
public class SqlCollection : ICollectionFixture<SqlServerFixture>;

/// <summary>The sample 835 through the real handler and a real SQL Server (Testcontainers).</summary>
[Collection("sql")]
public class RemittanceIngestTests(SqlServerFixture sql)
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name));

    private async Task<RemittanceDb> FreshDbAsync()
    {
        var db = new RemittanceDb(new DbContextOptionsBuilder<RemittanceDb>()
            .UseSqlServer(sql.ConnectionString("cb_remit_" + Guid.NewGuid().ToString("N")[..8])).Options);
        await db.Database.MigrateAsync();
        await ReasonCodeSeeder.SeedAsync(db);
        return db;
    }

    [DockerFact]
    public async Task Sample_835_posts_matched_claims_and_parks_the_unknown_one()
    {
        await using var db = await FreshDbAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2025, 11, 4, 9, 0, 0, TimeSpan.Zero));
        var org = Guid.NewGuid();
        foreach (var (number, charge) in new[] { ("CB250100120", 230m), ("CB250100118", 1350m), ("CB250100099", 180m) })
            db.ExpectedClaims.Add(new ExpectedClaim
            {
                ClaimId = Guid.NewGuid(), ClaimNumber = number, OrgId = org, PayerId = "PAYER-B", TotalCharge = charge,
                SubmittedAt = clock.GetUtcNow().AddDays(-15), UpdatedAt = clock.GetUtcNow()
            });
        await db.SaveChangesAsync();

        var outbox = new RecordingOutbox();
        var handler = new RemittanceReceivedHandler(db, new SampleBlobs(Sample("remittance.835.x12")),
            new PostingService(db, outbox, new RemittanceMetrics(new TestMeterFactory()), clock), clock,
            NullLogger<RemittanceReceivedHandler>.Instance);
        var fileId = Guid.NewGuid();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await handler.HandleAsync(EventEnvelope.Create(new RemittanceReceived(fileId, "PAYER-B", "remittances", "x.835", null, clock.GetUtcNow()), "test"),
                "payers.remittance-received", default);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        var file = await db.Files.SingleAsync(f => f.Id == fileId);
        file.ShouldSatisfyAllConditions(f => f.MatchedCount.ShouldBe(3), f => f.UnmatchedCount.ShouldBe(1), f => f.Status.ShouldBe("PartiallyPosted"),
            f => f.EftTraceNumber.ShouldBe("EFT2025110300017"));
        (await db.Postings.Select(p => p.Kind).ToListAsync()).ShouldBe(["PartialPayment", "Denial", "Reversal"], ignoreOrder: true);
        (await db.Unmatched.SingleAsync()).ClaimNumber.ShouldBe("CB259999999");
        (await db.PostingAdjustments().CountAsync(a => a.GroupCode == "PR")).ShouldBeGreaterThan(0);
        outbox.Events.Select(e => e.GetType().Name).ShouldBe(["PaymentPosted", "ClaimDenied", "ReversalPosted"]);

        // same file again (Kafka redelivery) is a no-op
        await handler.HandleAsync(EventEnvelope.Create(new RemittanceReceived(fileId, "PAYER-B", "remittances", "x.835", null, clock.GetUtcNow()), "test"),
            "payers.remittance-received", default);
        (await db.Postings.CountAsync()).ShouldBe(3);
    }

    [DockerFact]
    public async Task Unreadable_835_is_recorded_not_thrown()
    {
        await using var db = await FreshDbAsync();
        var clock = new FakeTimeProvider();
        var handler = new RemittanceReceivedHandler(db, new SampleBlobs(Sample("malformed.835.x12")),
            new PostingService(db, new RecordingOutbox(), new RemittanceMetrics(new TestMeterFactory()), clock), clock,
            NullLogger<RemittanceReceivedHandler>.Instance);
        var id = Guid.NewGuid();
        await handler.HandleAsync(EventEnvelope.Create(new RemittanceReceived(id, "PAYER-B", "r", "bad.835", null, clock.GetUtcNow()), "t"), "t", default);
        await db.SaveChangesAsync();
        (await db.Files.SingleAsync(f => f.Id == id)).Status.ShouldBe("Unreadable");
    }

    private sealed class SampleBlobs(string text) : IBlobStore
    {
        public Task<string> ReadTextAsync(BlobRef blob, CancellationToken ct = default) => Task.FromResult(text);
        public Task<BlobRef> UploadAsync(string c, string n, Stream s, string t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BlobRef> UploadTextAsync(string c, string n, string s, string t = "text/plain", CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(BlobRef blob, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListAsync(string c, string p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BlobRef> MoveAsync(BlobRef s, string t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(BlobRef blob, CancellationToken ct = default) => Task.FromResult(true);
    }
}

internal static class DbExtensions
{
    public static IQueryable<PostingAdjustment> PostingAdjustments(this RemittanceDb db) => db.Set<PostingAdjustment>();
}
