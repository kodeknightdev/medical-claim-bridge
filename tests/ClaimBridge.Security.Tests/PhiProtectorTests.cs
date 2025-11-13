using System.Security.Cryptography;
using ClaimBridge.ServiceDefaults.Phi;

namespace ClaimBridge.Security.Tests;

public class PhiProtectorTests
{
    private readonly AesGcmPhiProtector _protector = new(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public void Round_trips_and_never_stores_plaintext()
    {
        var sealedValue = _protector.Protect("NWH1000002");
        sealedValue.ShouldStartWith("v1:");
        sealedValue.ShouldNotContain("NWH1000002");
        _protector.Unprotect(sealedValue).ShouldBe("NWH1000002");
    }

    [Fact]
    public void Same_value_encrypts_differently_each_time() =>
        _protector.Protect("1958-02-12").ShouldNotBe(_protector.Protect("1958-02-12"));

    [Fact]
    public void Tampering_is_detected()
    {
        var bytes = Convert.FromBase64String(_protector.Protect("secret")[3..]);
        bytes[^1] ^= 0xFF;
        Should.Throw<CryptographicException>(() => _protector.Unprotect("v1:" + Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Lookup_hash_is_deterministic_and_normalised()
    {
        _protector.Hash(" nwh1000002 ").ShouldBe(_protector.Hash("NWH1000002"));
        _protector.Hash("NWH1000002").ShouldNotBe(_protector.Hash("NWH1000003"));
    }

    [Fact]
    public void A_different_key_cannot_read_the_value()
    {
        var other = new AesGcmPhiProtector(new byte[32]);
        Should.Throw<CryptographicException>(() => other.Unprotect(_protector.Protect("NWH1000002")));
        other.Hash("NWH1000002").ShouldNotBe(_protector.Hash("NWH1000002"));
    }

    [Fact]
    public void Key_must_be_32_bytes() => Should.Throw<ArgumentException>(() => new AesGcmPhiProtector(new byte[16]));

    [Fact]
    public void Mask_tail_keeps_the_last_four() => "NWH1000002".MaskTail().ShouldBe("******0002");
}
