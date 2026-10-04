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
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            if (line.Trim() == "exit") break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            PlaytestObservation observation = runner.ExecuteLine(line);
            Print(observation);
            if (observation.Quit) break;
        }
    }
}
