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
        if (!args.Contains("--compatibility-avalonia", StringComparer.Ordinal))
        {
            return StrategyNativeRuntime.Run(args.Contains("--launch-smoke", StringComparer.Ordinal));
        }
        StrategyApplication.Smoke = args.Contains("--launch-smoke", StringComparer.Ordinal);
        Avalonia.AppBuilder.Configure<StrategyApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        return StrategyApplication.ExitCode;
    }
}
