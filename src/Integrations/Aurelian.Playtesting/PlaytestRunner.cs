using System.Globalization;
using System.Text.Json;
using InputMan.Core;

namespace Aurelian.Playtesting;

public class PlaytestRunner<TObservation>(
    IPlaytestTarget<TObservation> target,
    string outputDirectory,
    System.Text.Json.Serialization.Metadata.JsonTypeInfo<TObservation> observationMetadata) : IDisposable
    where TObservation : class
{
    private readonly HashSet<KeyboardKey> held = [];
    private long ticks;
    private int index;
    private readonly string output = Path.GetFullPath(outputDirectory);
    private StreamWriter? trace;
    private readonly List<PlaytestStep> inputs = [];
    private readonly Dictionary<string, PlaytestStep[]> checkpoints = new(StringComparer.Ordinal);
    private bool replaying;

    public bool Quit => target.Quit;

    public void Print(TObservation observation, TextWriter? writer = null)
    {
        PlaytestConsole.Print(observation, observationMetadata, writer);
    }

    private bool ExecuteAndCheckQuit(PlaytestStep step)
    {
        Execute(step);
        return target.Quit;
    }

    public void Run(PlaytestScript script)
    {
        PlaytestScripts.Validate(script);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "script.json"), JsonSerializer.Serialize(script,
            PlaytestScriptJsonContext.Default.PlaytestScript));
        try
        {
            foreach (PlaytestStep step in script.Steps)
            {
                if (ExecuteAndCheckQuit(step))
                {
                    break;
                }
            }
        }
        finally
        {
            ReleaseAll();
            WriteFinal();
        }
    }

    public TObservation Execute(PlaytestStep step)
    {
        PlaytestScripts.Validate(new PlaytestScript(1, [step]));
        try
        {
            switch (step.Kind)
            {
                case "checkpoint":
                    if (target is not IReplayablePlaytestTarget)
                    {
                        throw new NotSupportedException("This target does not support reset and input replay.");
                    }
                    checkpoints[step.Text!] = inputs.ToArray();
                    break;
                case "rewind":
                    Rewind(step.Text!);
                    break;
                case "mouse-delta":
                    target.MouseDelta(step.X, step.Y);
                    target.Frame(TimeSpan.Zero);
                    break;
                case "mouse-press":
                case "mouse-release":
                case "mouse-hold":
                    MouseButton button = PlaytestScripts.ParseMouseButton(step.Text!);
                    target.MouseButton(button, step.Kind != "mouse-release");
                    target.Frame(TimeSpan.Zero);
                    if (step.Kind == "mouse-hold")
                    {
                        Wait(step.Seconds);
                        target.MouseButton(button, false);
                        target.Frame(TimeSpan.Zero);
                    }
                    break;
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
                    if (!step.Focused)
                    {
                        held.Clear();
                    }
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
            TObservation observation = target.Observe();
            if (!replaying)
            {
                if (step.Kind is not ("checkpoint" or "rewind" or "capture"))
                {
                    inputs.Add(step with { Keys = step.Keys?.ToArray() });
                }
                WriteTrace(new PlaytestTrace<TObservation>(index++, step, observation, null));
            }
            return observation;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            WriteTrace(new PlaytestTrace<TObservation>(index, step, null, error.Message));
            ReleaseAll();
            throw new InvalidOperationException($"Step {index} ({step.Kind}) failed: {error.Message}", error);
        }
    }

    public TObservation ExecuteLine(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new FormatException("Empty command.");
        PlaytestStep step;
        switch (parts[0].ToLowerInvariant())
        {
            case "checkpoint" or "rewind" when parts.Length == 2:
                step = new(parts[0].ToLowerInvariant(), Text: parts[1]);
                break;
            case "mouse-delta" when parts.Length == 3:
                step = new("mouse-delta", X: double.Parse(parts[1], CultureInfo.InvariantCulture), Y: double.Parse(parts[2], CultureInfo.InvariantCulture));
                break;
            case "mouse-press" or "mouse-release" when parts.Length == 2:
                step = new(parts[0].ToLowerInvariant(), Text: parts[1]);
                break;
            case "mouse-hold" when parts.Length == 3:
                step = new("mouse-hold", Text: parts[1], Seconds: double.Parse(parts[2], CultureInfo.InvariantCulture));
                break;
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
        TObservation observation = Execute(step);
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

    private void Rewind(string name)
    {
        if (target is not IReplayablePlaytestTarget replayable)
        {
            throw new NotSupportedException("This target does not support reset and input replay.");
        }
        if (!checkpoints.TryGetValue(name, out PlaytestStep[]? prefix))
        {
            throw new InvalidOperationException($"Unknown checkpoint '{name}'.");
        }
        replayable.ResetForReplay();
        held.Clear();
        ticks = 0;
        replaying = true;
        try
        {
            foreach (PlaytestStep input in prefix)
            {
                Execute(input);
            }
        }
        finally
        {
            replaying = false;
        }
        inputs.Clear();
        inputs.AddRange(prefix);
    }

    private void SetKeys(string[] keys, bool down)
    {
        foreach (KeyboardKey key in keys.Select(PlaytestScripts.ParseKey).Distinct())
        {
            target.Key(key, down);
            if (down)
            {
                held.Add(key);
            }
            else
            {
                held.Remove(key);
            }
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

    private void WriteTrace(PlaytestTrace<TObservation> entry)
    {
        Directory.CreateDirectory(output);
        trace ??= new StreamWriter(Path.Combine(output, "trace.jsonl"), append: false);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", entry.Index);
            writer.WritePropertyName("step");
            JsonSerializer.Serialize(writer, entry.Step, PlaytestScriptJsonContext.Default.PlaytestStep);
            writer.WritePropertyName("observation");
            if (entry.Observation is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                JsonSerializer.Serialize(writer, entry.Observation, observationMetadata);
            }
            writer.WriteString("error", entry.Error);
            writer.WriteEndObject();
        }
        string json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        // Utf8JsonWriter owns compact formatting, including escaped multiline domain strings.
        trace.WriteLine(json);
        trace.Flush();
    }

    public void WriteFinal()
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "final.json"), JsonSerializer.Serialize(target.Observe(),
            observationMetadata));
        File.WriteAllText(Path.Combine(output, "inputs.json"), JsonSerializer.Serialize(new PlaytestScript(1, inputs.ToArray()),
            PlaytestScriptJsonContext.Default.PlaytestScript));
    }

    private void ReleaseAll()
    {
        foreach (KeyboardKey key in held)
        {
            target.Key(key, false);
        }
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

public sealed record PlaytestTrace<TObservation>(int Index, PlaytestStep Step, TObservation? Observation, string? Error)
    where TObservation : class;
