using TinyFarm.Core;

if (args.Contains("--llm-control", StringComparer.Ordinal))
{
    TinyFarmLlmControl.Run(args);
    return;
}

using var game = new TinyFarmGame(args);
game.Run();
