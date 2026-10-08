using Aurelian.Playtesting;
using PlaytestConsole = TinyFarm.Playtesting.PlaytestConsole;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Deliverance.Core.Storage;
using InputMan.Aurelian;
using InputMan.Core;
using TinyFarm.InputMan;
using TinyFarm.Playtesting;

namespace TinyFarm.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help"))
        {
            Console.WriteLine("TinyFarm LLM playtesting: run SCRIPT.json | fuzz --seed N --steps N | shell | <semantic command>\n"
                + "Options: --native --visible --output DIR --save-dir DIR --width N --height N\n"
                + "Shell: hold w+s 2, tap enter, click 0.5 0.5, click-action new-game, text turnip,\n"
                + "open inventory, open stats, save slot2.sav, load slot2.sav, inspect, mark NAME, assert unchanged NAME, exit.");
            return 0;
        }
        string output = Path.GetFullPath(Option(args, "--output") ?? "artifacts/tinyfarm-playtesting/session");
        string saves = Path.GetFullPath(Option(args, "--save-dir") ?? Path.Combine(output, "saves"));
        int width = int.Parse(Option(args, "--width") ?? "1920", CultureInfo.InvariantCulture);
        int height = int.Parse(Option(args, "--height") ?? "1080", CultureInfo.InvariantCulture);
        PlaytestScripts.Validate(new PlaytestScript(1, [new("resize", Width: width, Height: height)]));
        string[] positional = Positionals(args);
        bool shell = positional[0] == "shell";
        PlaytestScript? script = null;
        if (!shell)
        {
            if (positional[0] == "run")
            {
                if (positional.Length != 2) throw new FormatException("Use run SCRIPT.json.");
                script = PlaytestScripts.Read(positional[1]);
            }
            else if (positional[0] == "fuzz")
            {
                script = TinyFarmPlaytestProfiles.Fuzz(uint.Parse(Option(args, "--seed") ?? "1", CultureInfo.InvariantCulture),
                    int.Parse(Option(args, "--steps") ?? "100", CultureInfo.InvariantCulture));
            }
            else script = new PlaytestScript(1, [new("command", Command: string.Join(' ', positional))]);
            PlaytestScripts.Validate(script);
        }
        Directory.CreateDirectory(output);
        if (args.Contains("--native"))
        {
            string root = FindRoot();
            string binary = Path.Combine(root, "Games/TinyFarm/TinyFarm.Native/bin/Release/net10.0-windows/TinyFarm.Native.dll");
            if (!File.Exists(binary)) throw new FileNotFoundException("Build TinyFarm.Native in Release before using --native.", binary);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            start.ArgumentList.Add(binary);
            if (shell) start.ArgumentList.Add("--playtest-stdio");
            else
            {
                string scriptPath = Path.Combine(output, "request.json");
                File.WriteAllText(scriptPath, JsonSerializer.Serialize(script, PlaytestJsonContext.Default.PlaytestScript));
                start.ArgumentList.Add("--playtest-script");
                start.ArgumentList.Add(scriptPath);
            }
            AddOption(start, "--playtest-output", output);
            AddOption(start, "--playtest-save-dir", saves);
            AddOption(start, "--width", width.ToString(CultureInfo.InvariantCulture));
            AddOption(start, "--height", height.ToString(CultureInfo.InvariantCulture));
            if (args.Contains("--visible")) start.ArgumentList.Add("--playtest-visible");
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Native launch failed.");
            process.WaitForExit();
            return process.ExitCode;
        }
        var game = new TinyFarmGame(new FileSaveStore(saves), slice: true, crafting: true, shipping: true);
        using var target = new PlaytestTarget(game, new AurelianInputAdapter(new InputManEngine(GameControls.CreateProfile(true))), width, height);
        using var runner = new PlaytestRunner(target, output);
        if (shell) PlaytestConsole.Shell(runner);
        else
        {
            runner.Run(script!);
            PlaytestConsole.Print(target.Observe());
        }
        return 0;
    }

    private static void AddOption(ProcessStartInfo start, string name, string value)
    {
        start.ArgumentList.Add(name);
        start.ArgumentList.Add(value);
    }

    public static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 >= args.Length) throw new FormatException("Missing value for " + name);
        return args[index + 1];
    }

    private static string[] Positionals(string[] args)
    {
        var result = new List<string>();
        string[] valued = ["--output", "--save-dir", "--width", "--height", "--seed", "--steps"];
        for (int index = 0; index < args.Length; index++)
        {
            if (valued.Contains(args[index])) index++;
            else if (args[index] is "--native" or "--visible") continue;
            else if (args[index].StartsWith("--", StringComparison.Ordinal)) throw new FormatException("Unknown option " + args[index]);
            else result.Add(args[index]);
        }
        if (result.Count == 0) throw new FormatException("Missing command.");
        return result.ToArray();
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Games/TinyFarm/TinyFarm.Native"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Copeland checkout.");
    }
}
