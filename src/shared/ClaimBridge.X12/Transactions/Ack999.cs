using ClaimBridge.X12.Core;

namespace ClaimBridge.X12.Transactions;

public sealed record SetAcknowledgement(string ControlNumber, bool Accepted, IReadOnlyList<string> Errors);

/// <summary>Implementation acknowledgement for a whole 837 file. It says nothing about individual claims; 277CA does that.</summary>
public sealed record Ack999(string AcknowledgedGroupControlNumber, bool Accepted, IReadOnlyList<SetAcknowledgement> Sets)
{
    public const string Version = "005010X231A1";

    public static Ack999 Read(string text)
    {
        var interchange = X12Reader.Parse(text);
        if (interchange.TransactionType != "999")
            throw new X12ParseException($"Expected a 999, found {interchange.TransactionType}");

        string group = "";
        bool? accepted = null;
        var sets = new List<SetAcknowledgement>();
        string? setControl = null;
        var errors = new List<string>();

        foreach (var s in interchange.Transaction("999"))
        {
            switch (s.Id)
            {
                case "AK1":
                    group = s[2];
                    break;
                case "AK2":
                    setControl = s[2];
                    errors = [];
                    break;
                case "IK3":
                    errors.Add($"Segment {s[1]} at position {s[2]}: error {s[4]}");
                    break;
                case "IK4":
                    errors.Add($"Element {s[1]}: error {s[3]}");
                    break;
                case "IK5":
                    sets.Add(new SetAcknowledgement(setControl ?? "", s[1] is "A" or "E", errors));
                    break;
                case "AK9":
                    accepted = s[1] is "A" or "E" or "P";
                    break;
            }
        }
        if (accepted is null) throw new X12ParseException("999 has no AK9 segment");
        return new Ack999(group, accepted.Value, sets);
    }

    public string Write(InterchangeOptions options)
    {
        var w = new X12Writer(options, "FA", "999", Version);
        w.Add("AK1", "HC", AcknowledgedGroupControlNumber, Claim837PWriter.Version);
        foreach (var set in Sets)
        {
            w.Add("AK2", "837", set.ControlNumber, Claim837PWriter.Version);
            foreach (var error in set.Errors)
                w.Add("IK3", "CLM", "1", "", "8").Add("IK4", "1", "", "7", X12Writer.Clean(error));
            w.Add("IK5", set.Accepted ? "A" : "R");
        }
        var acceptedSets = Sets.Count(s => s.Accepted);
        w.Add("AK9", Accepted ? (acceptedSets == Sets.Count ? "A" : "P") : "R", Sets.Count.ToString(), Sets.Count.ToString(),
            acceptedSets.ToString());
        return w.Build();
    }
}
