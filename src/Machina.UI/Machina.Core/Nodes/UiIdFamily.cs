using Machina.Layout.Rows;

namespace Machina.Core.Nodes;

/// <summary>A namespace of stable node IDs indexed by a domain key type.</summary>
public sealed class UiIdFamily<TKey> where TKey : notnull
{
    private readonly NodeId scope;
    private readonly Func<TKey, string> format;

    public UiIdFamily(NodeId scope, Func<TKey, string> format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Value);
        ArgumentNullException.ThrowIfNull(format);
        this.scope = scope;
        this.format = format;
    }

    public NodeId this[TKey key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return scope.Child(format(key));
        }
    }
}
