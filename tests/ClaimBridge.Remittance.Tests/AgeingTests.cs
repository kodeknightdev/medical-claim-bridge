using ClaimBridge.Remittance.Data;
using ClaimBridge.Remittance.Domain;

namespace ClaimBridge.Remittance.Tests;

public class AgeingTests
{
    private static readonly DateOnly AsOf = new(2025, 12, 1);

    private static ClaimBalance Open(int ageDays, decimal outstanding, BalanceStatus status = BalanceStatus.Open) => new()
    {
        OpenedAt = new DateTimeOffset(AsOf.AddDays(-ageDays).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        PayerOutstanding = outstanding, Status = status
    };

    [Theory]
    [InlineData(0, "0-30")]
    [InlineData(30, "0-30")]
    [InlineData(31, "31-60")]
    [InlineData(60, "31-60")]
    [InlineData(61, "61-90")]
    [InlineData(91, "90+")]
    public void Buckets_follow_days_since_submission(int days, string bucket) =>
        Ageing.BucketFor(new DateTimeOffset(AsOf.AddDays(-days).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), AsOf).ShouldBe(bucket);

    [Fact]
    public void Paid_and_closed_balances_are_not_aged()
    {
        var buckets = Ageing.Calculate([Open(10, 100), Open(45, 200), Open(75, 50, BalanceStatus.Denied), Open(120, 400),
            Open(5, 999, BalanceStatus.Paid), Open(5, 0)], AsOf);
        buckets.ShouldBe(new AgeingBuckets(100, 200, 50, 400));
        buckets.Total.ShouldBe(750);
    }

    [Fact]
    public void Days_in_ar_is_outstanding_over_average_daily_charges()
    {
        Ageing.DaysInAr(45_000, 90_000).ShouldBe(45);
        Ageing.DaysInAr(1000, 0).ShouldBe(0);
    }
}
