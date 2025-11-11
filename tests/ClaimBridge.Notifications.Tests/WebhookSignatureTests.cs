using ClaimBridge.Contracts.Webhooks;

namespace ClaimBridge.Notifications.Tests;

public class WebhookSignatureTests
{
    private const string Secret = "whsec_test";
    private const string Body = """{"id":"1","type":"claim.paid","data":{"claimNumber":"CB250000001"}}""";
    private static readonly DateTimeOffset Now = new(2025, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Signature_has_timestamp_and_hex_hmac()
    {
        var header = WebhookSignature.Sign(Secret, Now.ToUnixTimeSeconds(), Body);
        header.ShouldStartWith($"t={Now.ToUnixTimeSeconds()},v1=");
        header.Split("v1=")[1].Length.ShouldBe(64);
    }

    [Fact]
    public void Matching_secret_and_body_verify()
    {
        var header = WebhookSignature.Sign(Secret, Now.ToUnixTimeSeconds(), Body);
        WebhookSignature.Verify(Secret, header, Body, Now.AddSeconds(30)).ShouldBeTrue();
    }

    [Fact]
    public void Tampered_body_or_wrong_secret_fail()
    {
        var header = WebhookSignature.Sign(Secret, Now.ToUnixTimeSeconds(), Body);
        WebhookSignature.Verify(Secret, header, Body.Replace("paid", "denied"), Now).ShouldBeFalse();
        WebhookSignature.Verify("whsec_other", header, Body, Now).ShouldBeFalse();
    }

    [Fact]
    public void Old_signatures_are_rejected_to_stop_replays()
    {
        var header = WebhookSignature.Sign(Secret, Now.ToUnixTimeSeconds(), Body);
        WebhookSignature.Verify(Secret, header, Body, Now.AddMinutes(6)).ShouldBeFalse();
        WebhookSignature.Verify(Secret, header, Body, Now.AddMinutes(-6)).ShouldBeFalse();
    }

    [Fact]
    public void Swapping_the_timestamp_breaks_the_signature()
    {
        var header = WebhookSignature.Sign(Secret, Now.ToUnixTimeSeconds(), Body);
        var forged = header.Replace($"t={Now.ToUnixTimeSeconds()}", $"t={Now.AddMinutes(1).ToUnixTimeSeconds()}");
        WebhookSignature.Verify(Secret, forged, Body, Now.AddMinutes(1)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1=abc")]
    [InlineData("t=notanumber,v1=abc")]
    public void Malformed_headers_fail(string? header) => WebhookSignature.Verify(Secret, header, Body, Now).ShouldBeFalse();

    [Fact]
    public void Any_of_several_v1_values_may_match_during_rotation()
    {
        var good = WebhookSignature.Sign(Secret, Now.ToUnixTimeSeconds(), Body);
        var header = $"t={Now.ToUnixTimeSeconds()},v1=deadbeef,{good.Split(',')[1]}";
        WebhookSignature.Verify(Secret, header, Body, Now).ShouldBeTrue();
    }
}
