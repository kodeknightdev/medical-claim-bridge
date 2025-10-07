using ClaimBridge.Remittance.Data;

namespace ClaimBridge.Remittance.Domain;

public sealed record AgeingBuckets(decimal Days0To30, decimal Days31To60, decimal Days61To90, decimal Days90Plus)
{
    public decimal Total => Days0To30 + Days31To60 + Days61To90 + Days90Plus;
}

public static class Ageing
{
    public static readonly string[] BucketNames = ["0-30", "31-60", "61-90", "90+"];

    /// <summary>Claims age from the day they were submitted to the payer, not from the date of service.</summary>
    public static string BucketFor(DateTimeOffset openedAt, DateOnly asOf)
    {
        var days = asOf.DayNumber - DateOnly.FromDateTime(openedAt.UtcDateTime).DayNumber;
        return days switch
        {
            <= 30 => "0-30",
            <= 60 => "31-60",
            <= 90 => "61-90",
            _ => "90+"
        };
    }

    public static AgeingBuckets Calculate(IEnumerable<ClaimBalance> open, DateOnly asOf)
    {
        decimal b1 = 0, b2 = 0, b3 = 0, b4 = 0;
        foreach (var balance in open.Where(b => b.PayerOutstanding > 0 && b.Status is BalanceStatus.Open or BalanceStatus.Denied))
        {
            switch (BucketFor(balance.OpenedAt, asOf))
            {
                case "0-30": b1 += balance.PayerOutstanding; break;
                case "31-60": b2 += balance.PayerOutstanding; break;
                case "61-90": b3 += balance.PayerOutstanding; break;
                default: b4 += balance.PayerOutstanding; break;
            }
        }
        return new AgeingBuckets(b1, b2, b3, b4);
    }

    /// <summary>Days in A/R = outstanding / average daily charges over the lookback window.</summary>
    public static decimal DaysInAr(decimal outstanding, decimal chargesInWindow, int windowDays = 90) =>
        chargesInWindow <= 0 ? 0 : Math.Round(outstanding / (chargesInWindow / windowDays), 1);
}
