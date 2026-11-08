using Machina.Layout.Rows;

namespace Machina.Runtime.State;

public enum UiStateLifetime
{
    Scope,
    Activation,
}

public sealed class UiState<T>(T value)
{
    public T Value { get; set; } = value;
}

/// <summary>
/// Transient widget state for one screen or modal entry. The host owns its lifetime.
/// Stable node IDs identify values; these values never belong to the saved model.
/// </summary>
public sealed class UiStateScope : IDisposable
{
    private readonly Dictionary<NodeId, object> values = new();
    private readonly HashSet<NodeId> activationValues = new();
    private readonly CancellationTokenSource cancellation = new();

    public UiStateScope()
    {
        Lifetime = cancellation.Token;
    }

    public bool IsDisposed { get; private set; }
    public int Count => values.Count;
    public CancellationToken Lifetime { get; }

    public T GetOrCreate<T>(NodeId id, Func<T> create, UiStateLifetime lifetime = UiStateLifetime.Scope) where T : class
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(create);
        if (values.TryGetValue(id, out object? value))
        {
            if (activationValues.Contains(id) != (lifetime == UiStateLifetime.Activation))
            {
                throw new InvalidOperationException($"Widget '{id}' changed its declared state lifetime.");
            }
            if (value is T typed)
            {
                return typed;
            }

            throw new InvalidOperationException($"Widget '{id}' already owns state of type '{value.GetType().Name}'.");
        }

        T state = create() ?? throw new InvalidOperationException($"Widget '{id}' produced null state.");
        values.Add(id, state);
        if (lifetime == UiStateLifetime.Activation)
        {
            activationValues.Add(id);
        }
        return state;
    }

    /// <summary>Forget input captures while preserving filters, selections and other screen values.</summary>
    public void Suspend()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        foreach (NodeId id in activationValues)
        {
            values.Remove(id);
        }
        activationValues.Clear();
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        try
        {
            cancellation.Cancel();
        }
        finally
        {
            values.Clear();
            activationValues.Clear();
            cancellation.Dispose();
        }
    }
}
