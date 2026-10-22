namespace Aurelian.StrategyDemo;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--proof", StringComparer.Ordinal))
        {
            return StrategyProof.Run();
        }
        Aurelian.Games.GameApp.Run(new MosswardGame(), args);
        return 0;
    }
}
