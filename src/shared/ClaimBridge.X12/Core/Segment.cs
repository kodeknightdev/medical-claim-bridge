namespace ClaimBridge.X12.Core;

/// <summary>One X12 segment. Element 0 is the segment id, so <c>this[1]</c> is the first data element (e.g. CLP01).</summary>
public sealed class Segment(string id, IReadOnlyList<string> elements, int index)
{
    public string Id { get; } = id;
    public IReadOnlyList<string> Elements { get; } = elements;
    public int Index { get; } = index;

    public string this[int position] => position < Elements.Count ? Elements[position] : "";

    public string[] Components(int position, char separator) =>
        this[position].Length == 0 ? [] : this[position].Split(separator);

    public override string ToString() => string.Join('*', Elements);
}

public sealed record X12Delimiters(char Element, char Component, char Repetition, char Segment)
{
    public static readonly X12Delimiters Default = new('*', ':', '^', '~');
}

public sealed class X12ParseException(string message, int segmentIndex = -1)
    : FormatException(segmentIndex >= 0 ? $"{message} (segment {segmentIndex + 1})" : message)
{
    public int SegmentIndex { get; } = segmentIndex;
}
