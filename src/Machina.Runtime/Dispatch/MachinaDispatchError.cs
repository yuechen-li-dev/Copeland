namespace Machina.Runtime.Dispatch;

public sealed class MachinaDispatchError : Exception
{
    public MachinaDispatchError(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
