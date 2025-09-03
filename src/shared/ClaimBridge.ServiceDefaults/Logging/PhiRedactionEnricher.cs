using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace ClaimBridge.ServiceDefaults.Logging;

/// <summary>Masks PHI-looking properties before any sink sees them.</summary>
public sealed partial class PhiRedactionEnricher : ILogEventEnricher
{
    public const string Mask = "[REDACTED]";

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "MemberId", "SubscriberId", "PatientName", "PatientFirstName", "PatientLastName", "FirstName", "LastName",
        "DateOfBirth", "Dob", "PatientDob", "Ssn", "Address", "Phone", "Email", "MemberName"
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToArray())
        {
            if (SensitiveNames.Contains(name))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(Mask)));
                continue;
            }
            var redacted = Redact(value);
            if (!ReferenceEquals(redacted, value))
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
        }
    }

    private static LogEventPropertyValue Redact(LogEventPropertyValue value) => value switch
    {
        StructureValue s when s.Properties.Any(p => SensitiveNames.Contains(p.Name) || p.Value is StructureValue) =>
            new StructureValue(s.Properties.Select(p => SensitiveNames.Contains(p.Name)
                ? new LogEventProperty(p.Name, new ScalarValue(Mask))
                : new LogEventProperty(p.Name, Redact(p.Value))), s.TypeTag),
        ScalarValue { Value: string str } when LooksLikeDob(str) => new ScalarValue(Mask),
        _ => value
    };

    // catches free-text DOBs that slip in as plain strings, e.g. "1984-03-07"
    private static bool LooksLikeDob(string s) => s.Length == 10 && DobPattern().IsMatch(s) &&
        DateOnly.TryParse(s, out var d) && d.Year < DateTime.UtcNow.Year - 1;

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DobPattern();
}
