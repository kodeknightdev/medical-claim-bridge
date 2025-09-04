namespace ClaimBridge.Contracts.Validation;

public static class Npi
{
    /// <summary>NPI check digit: Luhn over the 9-digit base prefixed with the card issuer id 80840.</summary>
    public static bool IsValid(string? npi)
    {
        if (npi is null || npi.Length != 10 || !npi.All(char.IsAsciiDigit)) return false;
        if (npi[0] != '1' && npi[0] != '2') return false;
        return CheckDigit(npi[..9]) == npi[9] - '0';
    }

    public static int CheckDigit(string base9)
    {
        var digits = "80840" + base9;
        var sum = 0;
        var doubleIt = true;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            doubleIt = !doubleIt;
        }
        return (10 - sum % 10) % 10;
    }
}

public static class TaxId
{
    // 00- prefix is never issued as an EIN, which keeps seeded ids obviously fake
    public static bool IsDemoRange(string? ein) => ein is not null && ein.Length == 10 && ein.StartsWith("00-");
}
