using System.Text.Json;
using System.Text.Json.Serialization;
using TinyFarm.Core;

internal static class TinyFarmLlmControl
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Run(string[] args)
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        var host = new TinyFarmSimulationHost(
            new TinyFarmSession(TinyFarmM20ControlStates.Create(definitions), definitions),
            definitions);
        string savePath = ReadOption(args, "--save-file")
            ?? Path.Combine(Environment.CurrentDirectory, "tiny-farm.save");
        WriteResponse("ready", null, host, definitions, []);

        while (Console.ReadLine() is string line)
        {
            string command = line.Trim();
            if (command.Length == 0)
            {
                continue;
            }

            try
            {
                if (command.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResponse("quit", null, host, definitions, []);
                    return;
                }

                if (command.Equals("inspect", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResponse("inspect", null, host, definitions, []);
                    continue;
                }

                if (command.StartsWith("scenario ", StringComparison.OrdinalIgnoreCase))
                {
                    string phase = command["scenario ".Length..].Trim();
                    host.ReplaceSession(new TinyFarmSession(TinyFarmM12ControlStates.Create(definitions, phase), definitions));
                    WriteResponse("scenario", phase, host, definitions, []);
                    continue;
                }

                if (command.Equals("save", StringComparison.OrdinalIgnoreCase))
                {
                    string fullPath = Path.GetFullPath(savePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    File.WriteAllBytes(fullPath, host.Session.CaptureWeekSave());
                    WriteResponse("saved", fullPath, host, definitions, []);
                    continue;
                }

                if (command.Equals("load", StringComparison.OrdinalIgnoreCase))
                {
                    string fullPath = Path.GetFullPath(savePath);
                    host.ReplaceSession(TinyFarmChunkedSaveCodec.Read(File.ReadAllBytes(fullPath), definitions));
                    WriteResponse("loaded", fullPath, host, definitions, []);
                    continue;
                }

                if (command.Equals("pause", StringComparison.OrdinalIgnoreCase)
                    || command.Equals("play", StringComparison.OrdinalIgnoreCase)
                    || command.Equals("fast-forward", StringComparison.OrdinalIgnoreCase)
                    || command.StartsWith("advance ", StringComparison.OrdinalIgnoreCase))
                {
                    host.Execute(TinyFarmSimulationCommandParser.Parse(command));
                    WriteResponse("controlled", command, host, definitions, []);
                    continue;
                }

                if (command.StartsWith("select-slot ", StringComparison.OrdinalIgnoreCase))
                {
                    string value = command["select-slot ".Length..].Trim();
                    if (!int.TryParse(value, out int slotNumber))
                    {
                        throw new FormatException("Expected select-slot <1-8>.");
                    }
                    TinyFarmStepResult selection = host.ExecuteIntent(
                        new SelectHotbarSlotIntent(new HotbarSlotId(slotNumber)));
                    WriteResponse("selected", command, host, definitions, selection.Narrative, selection.Results);
                    continue;
                }

                TinyFarmStepResult step = host.ExecuteIntent(TinyFarmCommandParser.Parse(command));
                WriteResponse("stepped", command, host, definitions, step.Narrative, step.Results);
            }
            catch (Exception exception) when (exception is FormatException or IOException or InvalidDataException or ArgumentOutOfRangeException)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    status = "error",
                    command,
                    error = exception.Message
                }, JsonOptions));
            }
        }
    }

    private static void WriteResponse(
        string status,
        string? command,
        TinyFarmSimulationHost host,
        TinyFarmDefinitions definitions,
        IReadOnlyList<NarrativeLine> narrative,
        IReadOnlyList<IntentResult>? results = null)
    {
        TinyFarmSession session = host.Session;
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(session.State, definitions, narrative);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status,
            command,
            stateHash = TinyFarmSemanticHash.Compute(session.State),
            projectionHash = TinyFarmFrameProjector.ComputeHash(frame),
            simulationMode = host.Mode,
            simulationSnapshot = host.Snapshot(),
            playerUi = session.State.Version >= TinyFarmState.PlayerUiSaveVersion
                ? TinyFarmPlayerUiProjector.Project(session.State, definitions)
                : null,
            results = results?.Select(result => new
            {
                actor = result.Envelope.Actor.Value,
                source = result.Envelope.Source,
                intent = result.Envelope.Intent.ToString(),
                result.Status,
                result.Reason,
                result.Events
            }).ToArray() ?? [],
            frame
        }, JsonOptions));
    }

    private static string? ReadOption(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
