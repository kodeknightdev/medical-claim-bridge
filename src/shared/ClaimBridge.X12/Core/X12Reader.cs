namespace ClaimBridge.X12.Core;

public sealed record X12Interchange(
    X12Delimiters Delimiters,
    string SenderId,
    string ReceiverId,
    string ControlNumber,
    string UsageIndicator,
    IReadOnlyList<Segment> Segments)
{
    /// <summary>Segments between ST and SE for the first (or only) transaction set.</summary>
    public IReadOnlyList<Segment> Transaction(string transactionSetId)
    {
        var st = Segments.FirstOrDefault(s => s.Id == "ST" && s[1] == transactionSetId)
                 ?? throw new X12ParseException($"No ST*{transactionSetId} transaction in interchange");
        var start = st.Index;
        var end = Segments.First(s => s.Id == "SE" && s.Index > start).Index;
        return Segments.Where(s => s.Index >= start && s.Index <= end).ToList();
    }

    public string TransactionType =>
        Segments.FirstOrDefault(s => s.Id == "ST")?[1] ?? throw new X12ParseException("Interchange has no ST segment");
}

/// <summary>Tokenises an interchange and validates the ISA/GS/ST envelopes. Handles only the subset claim-bridge uses.</summary>
public static class X12Reader
{
    public static X12Interchange Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new X12ParseException("File is empty");

        text = text.TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (!text.StartsWith("ISA", StringComparison.Ordinal) || text.Length < 106)
            throw new X12ParseException("File does not start with a complete ISA segment");

        // ISA is fixed width: element separator at 3, component separator at 104, segment terminator at 105
        var delimiters = new X12Delimiters(text[3], text[104], text[82], text[105]);

        var raw = text.Split(delimiters.Segment)
            .Select(s => s.Trim('\r', '\n', ' '))
            .Where(s => s.Length > 0)
            .ToList();

        var segments = new List<Segment>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var elements = raw[i].Split(delimiters.Element);
            if (elements[0].Length is < 2 or > 3 || !elements[0].All(char.IsAsciiLetterOrDigit))
                throw new X12ParseException($"Invalid segment id '{elements[0]}'", i);
            segments.Add(new Segment(elements[0], elements, i));
        }

        var isa = segments[0];
        if (isa.Elements.Count != 17)
            throw new X12ParseException($"ISA must have 16 elements, found {isa.Elements.Count - 1}", 0);

        ValidateEnvelopes(segments);
        return new X12Interchange(delimiters, isa[6].Trim(), isa[8].Trim(), isa[13], isa[15], segments);
    }

    private static void ValidateEnvelopes(List<Segment> segments)
    {
        var isa = segments[0];
        var iea = segments[^1];
        if (iea.Id != "IEA") throw new X12ParseException("Interchange is not closed by IEA", iea.Index);
        if (iea[2] != isa[13]) throw new X12ParseException($"IEA control number {iea[2]} does not match ISA {isa[13]}", iea.Index);

        var groups = 0;
        Segment? gs = null;
        Segment? st = null;
        var transactionsInGroup = 0;
        foreach (var s in segments)
        {
            switch (s.Id)
            {
                case "GS":
                    if (gs is not null) throw new X12ParseException("GS found before the previous group was closed", s.Index);
                    gs = s;
                    groups++;
                    transactionsInGroup = 0;
                    break;
                case "GE":
                    if (gs is null) throw new X12ParseException("GE without GS", s.Index);
                    if (s[2] != gs[6]) throw new X12ParseException($"GE control number {s[2]} does not match GS {gs[6]}", s.Index);
                    if (int.TryParse(s[1], out var declared) && declared != transactionsInGroup)
                        throw new X12ParseException($"GE declares {declared} transaction(s) but group has {transactionsInGroup}", s.Index);
                    gs = null;
                    break;
                case "ST":
                    if (gs is null) throw new X12ParseException("ST outside of a functional group", s.Index);
                    if (st is not null) throw new X12ParseException("ST found before the previous transaction was closed", s.Index);
                    st = s;
                    transactionsInGroup++;
                    break;
                case "SE":
                    if (st is null) throw new X12ParseException("SE without ST", s.Index);
                    var count = s.Index - st.Index + 1;
                    if (!int.TryParse(s[1], out var seCount) || seCount != count)
                        throw new X12ParseException($"SE segment count {s[1]} does not match actual {count}", s.Index);
                    if (s[2] != st[2]) throw new X12ParseException($"SE control number {s[2]} does not match ST {st[2]}", s.Index);
                    st = null;
                    break;
            }
        }

        if (st is not null) throw new X12ParseException("Transaction set is not closed by SE", st.Index);
        if (gs is not null) throw new X12ParseException("Functional group is not closed by GE", gs.Index);
        if (int.TryParse(iea[1], out var groupCount) && groupCount != groups)
            throw new X12ParseException($"IEA declares {groupCount} group(s) but interchange has {groups}", iea.Index);
    }
}
