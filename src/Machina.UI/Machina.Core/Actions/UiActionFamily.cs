namespace Machina.Core.Actions;

/// <summary>Constructs parameterized actions without formatting or parsing command strings.</summary>
public sealed class UiActionFamily<TKey, TMessage> where TKey : notnull where TMessage : notnull
{
    private readonly UiActionId scope;
    private readonly Func<TKey, string> format;
    private readonly Func<TKey, TMessage> message;

    public UiActionFamily(UiActionId scope, Func<TKey, string> format, Func<TKey, TMessage> message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Value);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(message);
        this.scope = scope;
        this.format = format;
        this.message = message;
    }

    public UiAction<TMessage> this[TKey key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return UiAction.Typed(scope.Child(format(key)), message(key));
        }
    }
}
