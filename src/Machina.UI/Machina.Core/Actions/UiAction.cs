namespace Machina.Core.Actions;

public sealed record UiAction(UiActionId Id)
{
    public string Name => Id.Value;

    public static UiAction Named(UiActionId id)
    {
        return new(id);
    }

    public static UiAction Named(string name)
    {
        return new(new UiActionId(name));
    }
}
