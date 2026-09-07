namespace Aurelian.Cli;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        return AurelianCli.RunAsync(args, Console.Out, Console.Error);
    }
}
