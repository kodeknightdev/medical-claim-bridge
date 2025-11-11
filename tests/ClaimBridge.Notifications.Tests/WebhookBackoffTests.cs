using ClaimBridge.Notifications.Webhooks;

namespace ClaimBridge.Notifications.Tests;

public class WebhookBackoffTests
{
    private readonly WebhookOptions _options = new();

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 30)]
    [InlineData(3, 120)]
    [InlineData(4, 600)]
    [InlineData(8, 21600)]
    [InlineData(20, 21600)]
    public void Delay_grows_with_each_failure_and_caps(int failures, int baseSeconds)
    {
        var delay = WebhookBackoff.NextDelay(failures, _options, new Random(1));
        delay.TotalSeconds.ShouldBeGreaterThanOrEqualTo(baseSeconds);
        delay.TotalSeconds.ShouldBeLessThanOrEqualTo(baseSeconds * 1.1);
    }

    [Fact]
    public void Attempt_budget_is_one_more_than_the_schedule() => WebhookBackoff.MaxAttempts(_options).ShouldBe(9);

    [Fact]
    public void Disable_threshold() =>
        (WebhookBackoff.ShouldDisable(14, _options), WebhookBackoff.ShouldDisable(15, _options)).ShouldBe((false, true));
}
