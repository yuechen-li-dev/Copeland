namespace Machina.Core.Actions;

public sealed record UiAction(string Name)
{
    public static UiAction Named(string name) => new(name);
}
