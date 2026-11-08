using System.Diagnostics.CodeAnalysis;

namespace Machina.Core.Actions;

public record UiAction(UiActionId Id)
{
    public string Name => Id.Value;
    protected virtual object? MessageValue => null;

    public bool TryGetMessage<TMessage>([MaybeNullWhen(false)] out TMessage message)
    {
        if (MessageValue is TMessage typed)
        {
            message = typed;
            return true;
        }
        message = default;
        return false;
    }

    public static UiAction<TMessage> Typed<TMessage>(UiActionId id, TMessage message) where TMessage : notnull
    {
        return new UiAction<TMessage>(id, message);
    }

    public static UiAction Named(UiActionId id)
    {
        return new(id);
    }

    public static UiAction Named(string name)
    {
        return new(new UiActionId(name));
    }
}

/// <summary>An immutable message carried unchanged through authoring, lowering and hit testing.</summary>
public sealed record UiAction<TMessage> : UiAction where TMessage : notnull
{
    public UiAction(UiActionId id, TMessage message) : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        ArgumentNullException.ThrowIfNull(message);
        Message = message;
    }

    public TMessage Message { get; }
    protected override object MessageValue => Message;
}
