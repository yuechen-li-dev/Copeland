namespace Machina.Core.Lowering;

public sealed class UiLoweringError : Exception
{
    public UiLoweringError(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
