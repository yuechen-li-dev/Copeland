using System.Text.Json;

namespace TinyFarm.Playtesting;

public static class PlaytestConsole
{
    public static void Print(PlaytestObservation observation)
    {
        Console.WriteLine(JsonSerializer.Serialize(observation, PlaytestJsonContext.Default.PlaytestObservation)
            .Replace("\r", "").Replace("\n", ""));
    }

    public static void Shell(PlaytestRunner runner)
    {
        Aurelian.Playtesting.PlaytestConsole.Shell(runner);
    }
}
