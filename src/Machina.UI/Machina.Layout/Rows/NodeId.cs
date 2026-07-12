namespace Machina.Layout.Rows;

public readonly record struct NodeId(string Value)
{
    public override string ToString() => Value;

    public static implicit operator NodeId(string value) => new(value);
}
