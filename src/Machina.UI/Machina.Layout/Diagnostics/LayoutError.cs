namespace Machina.Layout.Diagnostics;

public sealed class LayoutError : Exception
{
    public string Code { get; }

    public LayoutError(string code, string message)
        : base(message)
    {
        Code = code;
    }
}
