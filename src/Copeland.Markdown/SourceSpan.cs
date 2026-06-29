namespace Copeland.Markdown;

public readonly record struct SourceLocation(int Index, int Line, int Column)
{
    public override string ToString()
    {
        return $"{Line}:{Column}";
    }
}

public readonly record struct SourceSpan(
    int Start,
    int Length,
    SourceLocation StartLocation,
    SourceLocation EndLocation)
{
    public int End => Start + Length;

    public override string ToString()
    {
        return $"{StartLocation}-{EndLocation}";
    }
}
