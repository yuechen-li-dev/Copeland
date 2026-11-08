using Machina.Layout.Rows;

namespace Machina.Core.Actions;

public readonly record struct UiActionId
{
    public UiActionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Action identifier value must be non-empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public UiActionId Child(string segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Value);
        return new UiActionId(Value + ":" + IdSegment.Encode(segment));
    }

    public NodeId ToNodeId()
    {
        return new NodeId("action").Child(Value);
    }

    public UiAction ToAction()
    {
        return UiAction.Named(this);
    }

    public override string ToString()
    {
        return Value;
    }
}
