using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Aurelian.Playtesting;

public static class PlaytestConsole
{
    public static void Print<TObservation>(TObservation observation, JsonTypeInfo<TObservation> metadata,
        TextWriter? output = null)
    {
        (output ?? Console.Out).WriteLine(JsonSerializer.Serialize(observation, metadata).Replace("\r", "").Replace("\n", ""));
    }

    public static void Shell<TObservation>(PlaytestRunner<TObservation> runner,
        TextReader? input = null, TextWriter? output = null)
        where TObservation : class
    {
        string? line;
        while ((line = (input ?? Console.In).ReadLine()) is not null)
        {
            if (line.Trim() == "exit")
            {
                break;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            runner.Print(runner.ExecuteLine(line), output ?? Console.Out);
            if (runner.Quit)
            {
                break;
            }
        }
    }
}
