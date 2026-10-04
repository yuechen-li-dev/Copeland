using System.Text.Json;
using System.Text.Json.Serialization;
using InputMan.Core;

namespace TinyFarm.Playtesting;

public sealed record PlaytestScript(int SchemaVersion, PlaytestStep[] Steps, uint? Seed = null);

public sealed record PlaytestStep(
    string Kind,
    string[]? Keys = null,
    double Seconds = 0,
    double X = 0.5,
    double Y = 0.5,
    string? Text = null,
    string? Command = null,
    string? Path = null,
    int Width = 1920,
    int Height = 1080,
    bool Focused = true);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PlaytestScript))]
[JsonSerializable(typeof(PlaytestObservation))]
[JsonSerializable(typeof(PlaytestTrace))]
public partial class PlaytestJsonContext : JsonSerializerContext;

public static class PlaytestScripts
{
    public static PlaytestScript Read(string path)
    {
        PlaytestScript script = JsonSerializer.Deserialize(File.ReadAllText(path),
            PlaytestJsonContext.Default.PlaytestScript) ?? throw new FormatException("Script is null.");
        Validate(script);
        return script;
    }

    public static void Validate(PlaytestScript script)
    {
        if (script.SchemaVersion != 1 || script.Steps is null || script.Steps.Length > 10000)
        {
            throw new FormatException("Expected schemaVersion 1 and at most 10000 steps.");
        }
        double total = 0;
        foreach (PlaytestStep step in script.Steps)
        {
            if (step is null || !double.IsFinite(step.Seconds) || step.Seconds < 0 || step.Seconds > 60)
            {
                throw new FormatException("Each step duration must be finite, between 0 and 60 seconds.");
            }
            total += step.Seconds;
            switch (step.Kind)
            {
                case "hold":
                case "press":
                case "release":
                case "tap":
                    if (step.Keys is null || step.Keys.Length is < 1 or > 16)
                    {
                        throw new FormatException("Keyboard steps require 1 to 16 keys.");
                    }
                    foreach (string key in step.Keys) ParseKey(key);
                    break;
                case "click":
                case "pointer":
                case "scroll":
                    if (!double.IsFinite(step.X) || !double.IsFinite(step.Y)
                        || step.X < 0 || step.X > 1 || step.Y < 0 || step.Y > 1)
                    {
                        throw new FormatException("Pointer coordinates must be normalized to [0,1].");
                    }
                    if (step.Kind == "scroll" && (!float.TryParse(step.Text,
                        System.Globalization.CultureInfo.InvariantCulture, out float delta) || !float.IsFinite(delta)))
                    {
                        throw new FormatException("Scroll text must contain a finite wheel delta.");
                    }
                    break;
                case "command":
                    ArgumentException.ThrowIfNullOrWhiteSpace(step.Command);
                    break;
                case "text":
                case "click-action":
                    ArgumentException.ThrowIfNullOrWhiteSpace(step.Text);
                    break;
                case "capture":
                    ArgumentException.ThrowIfNullOrWhiteSpace(step.Path);
                    break;
                case "resize":
                    if (step.Width is < 320 or > 3840 || step.Height is < 240 or > 2160)
                    {
                        throw new FormatException("Viewport must be between 320x240 and 3840x2160.");
                    }
                    break;
                case "focus":
                case "wait":
                    break;
                default:
                    throw new FormatException($"Unknown step kind '{step.Kind}'.");
            }
        }
        if (total > 3600) throw new FormatException("Script exceeds one hour of simulation.");
    }

    public static KeyboardKey ParseKey(string text)
    {
        string name = text.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "up" => "ArrowUp",
            "down" => "ArrowDown",
            "left" => "ArrowLeft",
            "right" => "ArrowRight",
            "1" => "Number1",
            "2" => "Number2",
            "3" => "Number3",
            _ => text
        };
        if (!Enum.TryParse(name, true, out KeyboardKey key) || !Enum.IsDefined(key) || key == KeyboardKey.Unknown)
        {
            throw new FormatException($"Unknown keyboard key '{text}'.");
        }
        return key;
    }

    public static PlaytestScript Fuzz(uint seed, int count)
    {
        if (count is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(count));
        uint state = seed == 0 ? 0x6D2B79F5u : seed;
        uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
        string[] keys = ["w", "a", "s", "d", "e", "i", "c", "escape", "enter", "j", "space", "f9"];
        var steps = new List<PlaytestStep> { new("command", Command: "new game") };
        for (int index = 0; index < count; index++)
        {
            uint choice = Next() % 5;
            if (choice == 0)
            {
                steps.Add(new("click", X: (Next() % 1001) / 1000.0, Y: (Next() % 1001) / 1000.0));
            }
            else if (choice == 1)
            {
                steps.Add(new("hold", Keys: ["w", "s"], Seconds: (Next() % 30 + 1) / 60.0));
            }
            else
            {
                steps.Add(new("hold", Keys: [keys[Next() % keys.Length]], Seconds: (Next() % 30 + 1) / 60.0));
            }
        }
        return new PlaytestScript(1, steps.ToArray(), seed);
    }
}

