using System.Globalization;
using System.Text;

namespace ClaimBridge.X12.Core;

public sealed record InterchangeOptions(
    string SenderId,
    string ReceiverId,
    int ControlNumber,
    DateTimeOffset CreatedAt,
    string UsageIndicator = "T");

/// <summary>Builds an interchange with one functional group and one transaction set, filling in counts and control numbers.</summary>
public sealed class X12Writer
{
    private readonly X12Delimiters _d = X12Delimiters.Default;
    private readonly List<string> _transaction = [];
    private readonly InterchangeOptions _options;
    private readonly string _functionalId;
    private readonly string _transactionSet;
    private readonly string _version;

    public X12Writer(InterchangeOptions options, string functionalId, string transactionSet, string version)
    {
        _options = options;
        _functionalId = functionalId;
        _transactionSet = transactionSet;
        _version = version;
        Add("ST", transactionSet, "0001", version);
    }

    public X12Writer Add(string id, params string?[] elements)
    {
        // trailing empty elements are dropped, as X12 requires
        var values = elements.Select(e => e ?? "").ToList();
        while (values.Count > 0 && values[^1].Length == 0) values.RemoveAt(values.Count - 1);
        _transaction.Add(values.Count == 0 ? id : id + _d.Element + string.Join(_d.Element, values));
        return this;
    }

    public string Composite(params string?[] parts)
    {
        var values = parts.Select(p => p ?? "").ToList();
        while (values.Count > 0 && values[^1].Length == 0) values.RemoveAt(values.Count - 1);
        return string.Join(_d.Component, values);
    }

    public string Build()
    {
        var o = _options;
        var control = o.ControlNumber.ToString("D9", CultureInfo.InvariantCulture);
        var group = o.ControlNumber.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("ISA*00*          *00*          *ZZ*").Append(Pad(o.SenderId, 15)).Append("*ZZ*").Append(Pad(o.ReceiverId, 15))
            .Append('*').Append(o.CreatedAt.ToString("yyMMdd", CultureInfo.InvariantCulture))
            .Append('*').Append(o.CreatedAt.ToString("HHmm", CultureInfo.InvariantCulture))
            .Append("*^*00501*").Append(control).Append("*0*").Append(o.UsageIndicator).Append("*:~\n");
        sb.Append("GS*").Append(_functionalId).Append('*').Append(o.SenderId).Append('*').Append(o.ReceiverId).Append('*')
            .Append(o.CreatedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append('*')
            .Append(o.CreatedAt.ToString("HHmm", CultureInfo.InvariantCulture)).Append('*').Append(group).Append("*X*").Append(_version).Append("~\n");
        foreach (var s in _transaction) sb.Append(s).Append("~\n");
        sb.Append("SE*").Append(_transaction.Count + 1).Append("*0001~\n");
        sb.Append("GE*1*").Append(group).Append("~\n");
        sb.Append("IEA*1*").Append(control).Append("~\n");
        return sb.ToString();
    }

    public static string Date(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    public static string Amount(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Upper(string value) => Clean(value).ToUpperInvariant();

    /// <summary>Strips characters that would collide with delimiters.</summary>
    public static string Clean(string value) => new(value.Where(c => c is not ('*' or '~' or ':' or '^')).ToArray());

    private static string Pad(string value, int width) => value.Length >= width ? value[..width] : value.PadRight(width);
}

public static class X12Values
{
    public static decimal Amount(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    public static DateOnly? Date(string value) =>
        DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>ICD-10 codes travel without the dot in X12 (M17.11 becomes M1711).</summary>
    public static string IcdToX12(string code) => code.Replace(".", "");

    public static string IcdFromX12(string code) => code.Length > 3 ? code[..3] + "." + code[3..] : code;
}
