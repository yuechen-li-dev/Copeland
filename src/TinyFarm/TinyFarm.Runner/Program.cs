using TinyFarm.Core;

if (args.Contains("--repl", StringComparer.Ordinal))
{
    RunRepl();
    return;
}

TinyFarmM1Proof proof = TinyFarmCanonicalScenario.Prove();
string json = TinyFarmCanonicalScenario.WriteProofJson(proof);

int outputIndex = Array.IndexOf(args, "--proof-json");
if (outputIndex >= 0)
{
    if (outputIndex + 1 >= args.Length)
    {
        throw new ArgumentException("--proof-json requires an output path.");
    }

    string path = Path.GetFullPath(args[outputIndex + 1]);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, json + Environment.NewLine);
}

Console.WriteLine(json);
Environment.ExitCode = proof.Outcome == "A" ? 0 : 1;

static void RunRepl()
{
    var session = new TinyFarmSession(TinyFarmContent.CreateInitialState());
    IReadOnlyList<IntentResult> lastResults = [];
    Console.WriteLine("TinyFarm M1 — Headless Ariadne Adventure");

    while (true)
    {
        Console.WriteLine();
        Console.WriteLine(TinyFarmTextProjection.Describe(session.State));
        Console.Write("\n> ");
        string? command = Console.ReadLine();
        if (command is null || command.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (command.Equals("inspect", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(TinyFarmInspector.WriteJson(session, lastResults));
            continue;
        }

        try
        {
            TinyFarmStepResult step = session.Step(TinyFarmCommandParser.Parse(command));
            lastResults = step.Results;
            foreach (IntentResult result in step.Results.Where(result => result.Envelope.Source == IntentSourceKind.Human))
            {
                Console.WriteLine($"{result.Status}: {result.Reason}");
            }

            foreach (NarrativeLine line in step.Narrative)
            {
                Console.WriteLine($"{line.Speaker}: {line.Text}");
            }
        }
        catch (FormatException exception)
        {
            Console.WriteLine(exception.Message);
        }
    }
}
