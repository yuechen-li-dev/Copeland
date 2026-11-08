using Machina.Layout.Rows;

namespace Machina.Runtime.State;

/// <summary>A widget slot whose value type and lifetime are declared once.</summary>
public readonly record struct UiStateKey<T> where T : class
{
    public UiStateKey(NodeId id, UiStateLifetime lifetime = UiStateLifetime.Scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        Id = id;
        Lifetime = lifetime;
    }

    public NodeId Id { get; }
    public UiStateLifetime Lifetime { get; }
}
