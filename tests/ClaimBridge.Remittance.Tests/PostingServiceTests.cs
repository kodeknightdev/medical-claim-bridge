using ClaimBridge.Contracts.Events;
using ClaimBridge.Remittance.Data;
using ClaimBridge.Remittance.Domain;
using ClaimBridge.ServiceDefaults.Messaging;
using ClaimBridge.X12.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace ClaimBridge.Remittance.Tests;

public class PostingServiceTests
{
    private readonly RemittanceDb _db = new(new DbContextOptionsBuilder<RemittanceDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly RecordingOutbox _outbox = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2025, 11, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly RemittanceFile _file = new() { Id = Guid.NewGuid(), PayerId = "PAYER-B", EftTraceNumber = "EFT1", PaymentDate = new DateOnly(2025, 11, 10) };

    private PostingService Service() => new(_db, _outbox, new RemittanceMetrics(new TestMeterFactory()), _clock);

    private async Task<ExpectedClaim> Expect(string number, decimal charge)
    {
        var claim = new ExpectedClaim
        {
            ClaimId = Guid.NewGuid(), ClaimNumber = number, OrgId = Guid.NewGuid(), PayerId = "PAYER-B", TotalCharge = charge,
            SubmittedAt = _clock.GetUtcNow().AddDays(-20)
        };
        _db.ExpectedClaims.Add(claim);
        _db.Files.Add(_file);
        await _db.SaveChangesAsync();
        return claim;
    }

    [Fact]
    public async Task Payment_posts_updates_the_balance_and_publishes_payment_posted()
    {
        var claim = await Expect("CB1", 200);
        (await Service().PostAsync(_file, new ClaimPayment("CB1", "1", 200, 120, 40, "P", [new("CO", "45", 40), new("PR", "2", 40)], [], []), default))
            .ShouldBeTrue();
        await _db.SaveChangesAsync();

        var balance = await _db.Balances.SingleAsync();
        balance.Status.ShouldBe(BalanceStatus.Paid);
        balance.PayerOutstanding.ShouldBe(0);
        _outbox.Events.OfType<PaymentPosted>().Single().ClaimId.ShouldBe(claim.ClaimId);
    }

    [Fact]
    public async Task Unknown_claim_numbers_go_to_the_reconciliation_queue_without_failing()
    {
        await Expect("CB1", 200);
        (await Service().PostAsync(_file, new ClaimPayment("CB-UNKNOWN", "1", 75, 60, 0, null, [new("CO", "45", 15)], [], []), default))
            .ShouldBeFalse();
        await _db.SaveChangesAsync();

        (await _db.Unmatched.SingleAsync()).Reason.ShouldContain("No submitted claim");
        _outbox.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Lines_that_do_not_balance_are_parked_for_ops()
    {
        await Expect("CB1", 200);
        (await Service().PostAsync(_file, new ClaimPayment("CB1", "1", 200, 150, 0, null, [new("CO", "45", 40)], [], []), default)).ShouldBeFalse();
        await _db.SaveChangesAsync();
        (await _db.Unmatched.SingleAsync()).Reason.ShouldContain("Does not balance");
    }

    [Fact]
    public async Task Takeback_reverses_the_earlier_payment_and_reopens_the_balance()
    {
        await Expect("CB1", 180);
        var service = Service();
        await service.PostAsync(_file, new ClaimPayment("CB1", "1", 180, 120, 24, "P", [new("CO", "45", 36), new("PR", "2", 24)], [], []), default);
        await _db.SaveChangesAsync();
        await service.PostAsync(_file, new ClaimPayment("CB1", "22", -180, -120, -24, "P", [new("CO", "45", -36), new("PR", "2", -24)], [], []), default);
        await _db.SaveChangesAsync();

        var balance = await _db.Balances.SingleAsync();
        balance.Paid.ShouldBe(0);
        balance.Status.ShouldBe(BalanceStatus.Open);
        balance.PayerOutstanding.ShouldBe(180);
        _outbox.Events.OfType<ReversalPosted>().Single().ReversedAmount.ShouldBe(120);
    }

    [Fact]
    public async Task Denial_publishes_claim_denied_with_the_category()
    {
        await Expect("CB1", 1350);
        await Service().PostAsync(_file, new ClaimPayment("CB1", "4", 1350, 0, 0, null, [new("CO", "197", 1350)], [], ["N54"]), default);
        var denied = _outbox.Events.OfType<ClaimDenied>().Single();
        denied.CarcCode.ShouldBe("197");
        denied.Category.ShouldBe("Authorization");
        denied.RarcCodes.ShouldBe(["N54"]);
    }
}

public sealed class RecordingOutbox : IEventOutbox
{
    public List<object> Events { get; } = [];

    public EventEnvelope Enqueue<T>(string topic, string key, T @event, string? correlationId = null) where T : IIntegrationEvent
    {
        Events.Add(@event);
        return EventEnvelope.Create(@event, "test");
    }
}

public sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
{
    public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new(options);
    public void Dispose() { }
}
