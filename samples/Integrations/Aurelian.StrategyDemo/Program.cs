using Avalonia;
namespace Aurelian.StrategyDemo;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--proof", StringComparer.Ordinal))
        {
            return StrategyProof.Run();
        }
        StrategyApplication.Smoke = args.Contains("--launch-smoke", StringComparer.Ordinal);
        Avalonia.AppBuilder.Configure<StrategyApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        return StrategyApplication.ExitCode;
    }
}
