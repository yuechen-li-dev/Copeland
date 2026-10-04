using System.Globalization;
using System.Text.Json;
using InputMan.Core;

namespace TinyFarm.Playtesting;

public sealed class PlaytestRunner(PlaytestTarget target, string outputDirectory) : IDisposable
{
    private readonly HashSet<KeyboardKey> held = [];
    private long ticks;
    private int index;
    private readonly string output = Path.GetFullPath(outputDirectory);
    private StreamWriter? trace;

    public void Run(PlaytestScript script)
    {
        PlaytestScripts.Validate(script);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "script.json"), JsonSerializer.Serialize(script,
            PlaytestJsonContext.Default.PlaytestScript));
        try
        {
            foreach (PlaytestStep step in script.Steps)
            {
                if (Execute(step).Quit) break;
            }
        }
        finally
        {
            ReleaseAll();
            WriteFinal();
        }
    }

    public PlaytestObservation Execute(PlaytestStep step)
    {
        PlaytestScripts.Validate(new PlaytestScript(1, [step]));
        try
        {
            switch (step.Kind)
            {
                case "press":
                    SetKeys(step.Keys!, true);
                    target.Frame(TimeSpan.Zero);
                    break;
                case "release":
                    SetKeys(step.Keys!, false);
                    target.Frame(TimeSpan.Zero);
                    break;
                case "hold":
                case "tap":
                    SetKeys(step.Keys!, true);
                    target.Frame(TimeSpan.Zero);
                    Wait(step.Seconds);
                    SetKeys(step.Keys!, false);
                    target.Frame(TimeSpan.Zero);
                    break;
                case "wait":
                    Wait(step.Seconds);
                    break;
                case "pointer":
                    target.Pointer(step.X, step.Y);
                    target.Frame(TimeSpan.Zero);
                    break;
                case "click":
                    Click(step.X, step.Y);
                    break;
                case "click-action":
                    (double x, double y) = target.ActionPoint(step.Text!);
                    Click(x, y);
                    break;
                case "text":
                    target.Text(step.Text!);
                    target.Frame(TimeSpan.Zero);
                    break;
                case "scroll":
                    target.Scroll(step.X, step.Y, float.Parse(step.Text!, CultureInfo.InvariantCulture));
                    target.Frame(TimeSpan.Zero);
                    break;
                case "command":
                    target.Command(step.Command!);
                    target.Frame(TimeSpan.Zero);
                    break;
                case "focus":
                    if (!step.Focused) held.Clear();
                    target.Focus(step.Focused);
                    target.Frame(TimeSpan.Zero);
                    break;
                case "resize":
                    target.Resize(step.Width, step.Height);
                    target.Frame(TimeSpan.Zero);
                    break;
                case "capture":
                    target.Capture(ArtifactPath(step.Path!));
                    break;
            }
            target.SettlePersistence();
            PlaytestObservation observation = target.Observe();
            WriteTrace(new PlaytestTrace(index++, step, observation, null));
            return observation;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            WriteTrace(new PlaytestTrace(index, step, null, error.Message));
            ReleaseAll();
            throw new InvalidOperationException($"Step {index} ({step.Kind}) failed: {error.Message}", error);
        }
    }

    public PlaytestObservation ExecuteLine(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new FormatException("Empty command.");
        PlaytestStep step;
        switch (parts[0].ToLowerInvariant())
        {
            case "hold" when parts.Length == 3:
                step = new("hold", Keys: parts[1].Split('+'), Seconds: double.Parse(parts[2], CultureInfo.InvariantCulture));
                break;
            case "tap" or "press" or "release" when parts.Length == 2:
                step = new(parts[0].ToLowerInvariant(), Keys: parts[1].Split('+'));
                break;
            case "click" when parts.Length == 3:
                step = new("click", X: double.Parse(parts[1], CultureInfo.InvariantCulture), Y: double.Parse(parts[2], CultureInfo.InvariantCulture));
                break;
            case "click-action" when parts.Length == 2:
                step = new("click-action", Text: parts[1]);
                break;
            case "text":
                step = new("text", Text: line[(line.IndexOf(' ') + 1)..]);
                break;
            case "wait-seconds" when parts.Length == 2:
                step = new("wait", Seconds: double.Parse(parts[1], CultureInfo.InvariantCulture));
                break;
            case "capture" when parts.Length == 2:
                step = new("capture", Path: parts[1]);
                break;
            case "resize" when parts.Length == 3:
                step = new("resize", Width: int.Parse(parts[1], CultureInfo.InvariantCulture), Height: int.Parse(parts[2], CultureInfo.InvariantCulture));
                break;
            case "focus" when parts.Length == 2 && parts[1] is "on" or "off":
                step = new("focus", Focused: parts[1] == "on");
                break;
            default:
                step = new("command", Command: line);
                break;
        }
        PlaytestObservation observation = Execute(step);
        WriteFinal();
        return observation;
    }

    private void Wait(double seconds)
    {
        int frames = (int)Math.Ceiling(seconds * 60 - 1e-9);
        for (int frame = 0; frame < frames; frame++)
        {
            long before = ticks * TimeSpan.TicksPerSecond / 60;
            long after = ++ticks * TimeSpan.TicksPerSecond / 60;
            target.Frame(TimeSpan.FromTicks(after - before));
        }
    }

    private void SetKeys(string[] keys, bool down)
    {
        foreach (KeyboardKey key in keys.Select(PlaytestScripts.ParseKey).Distinct())
        {
            target.Key(key, down);
            if (down) held.Add(key);
            else held.Remove(key);
        }
    }

    private void Click(double x, double y)
    {
        target.Pointer(x, y, true);
        target.Frame(TimeSpan.Zero);
        target.Pointer(x, y, false);
        target.Frame(TimeSpan.Zero);
    }

    private string ArtifactPath(string relative)
    {
        string path = Path.GetFullPath(Path.Combine(output, relative));
        if (!path.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Capture path must stay within --output.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private void WriteTrace(PlaytestTrace entry)
    {
        Directory.CreateDirectory(output);
        trace ??= new StreamWriter(Path.Combine(output, "trace.jsonl"), append: false);
        string json = JsonSerializer.Serialize(entry, PlaytestJsonContext.Default.PlaytestTrace);
        // Keep JSONL genuinely one record per line without losing string escapes.
        using JsonDocument document = JsonDocument.Parse(json);
        trace.WriteLine(document.RootElement.GetRawText().Replace("\r", "").Replace("\n", ""));
        trace.Flush();
    }

    public void WriteFinal()
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "final.json"), JsonSerializer.Serialize(target.Observe(),
            PlaytestJsonContext.Default.PlaytestObservation));
    }

    private void ReleaseAll()
    {
        foreach (KeyboardKey key in held) target.Key(key, false);
        held.Clear();
        target.ReleaseMouse();
        target.Frame(TimeSpan.Zero);
    }

    public void Dispose()
    {
        ReleaseAll();
        WriteFinal();
        trace?.Dispose();
    }
}
