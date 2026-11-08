namespace Machina.Layout.Rows;

public readonly record struct NodeId(string Value)
{
    public NodeId Child(string segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Value);
        return new NodeId(Value + "." + IdSegment.Encode(segment));
    }

    public NodeId Child(int index)
    {
        return Child(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public bool IsWithin(NodeId scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Value);
        return !string.IsNullOrEmpty(Value)
            && (Value == scope.Value || Value.StartsWith(scope.Value + ".", StringComparison.Ordinal));
    }

    public override string ToString() => Value;

    public static implicit operator NodeId(string value) => new(value);
}
