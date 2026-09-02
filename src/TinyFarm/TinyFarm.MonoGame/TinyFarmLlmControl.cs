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
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
        var session = new TinyFarmSession(TinyFarmContent.CreateSceneState(definitions), definitions);
        string savePath = ReadOption(args, "--save-file")
            ?? Path.Combine(Environment.CurrentDirectory, "tiny-farm.save");
        WriteResponse("ready", null, session, definitions, []);

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
                    WriteResponse("quit", null, session, definitions, []);
                    return;
                }

                if (command.Equals("inspect", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResponse("inspect", null, session, definitions, []);
                    continue;
                }

                if (command.Equals("save", StringComparison.OrdinalIgnoreCase))
                {
                    string fullPath = Path.GetFullPath(savePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    File.WriteAllBytes(fullPath, session.CaptureWeekSave());
                    WriteResponse("saved", fullPath, session, definitions, []);
                    continue;
                }

                if (command.Equals("load", StringComparison.OrdinalIgnoreCase))
                {
                    string fullPath = Path.GetFullPath(savePath);
                    session = TinyFarmChunkedSaveCodec.Read(File.ReadAllBytes(fullPath), definitions);
                    WriteResponse("loaded", fullPath, session, definitions, []);
                    continue;
                }

                TinyFarmStepResult step = session.Step(TinyFarmCommandParser.Parse(command));
                WriteResponse("stepped", command, session, definitions, step.Narrative, step.Results);
            }
            catch (Exception exception) when (exception is FormatException or IOException or InvalidDataException)
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
        TinyFarmSession session,
        TinyFarmDefinitions definitions,
        IReadOnlyList<NarrativeLine> narrative,
        IReadOnlyList<IntentResult>? results = null)
    {
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(session.State, definitions, narrative);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status,
            command,
            stateHash = TinyFarmSemanticHash.Compute(session.State),
            projectionHash = TinyFarmFrameProjector.ComputeHash(frame),
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
