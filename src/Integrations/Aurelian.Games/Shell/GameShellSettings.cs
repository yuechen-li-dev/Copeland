using System.Text.Json.Serialization;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Games;

public enum GameWindowMode
{
    Windowed,
    Borderless,
    Fullscreen,
}

public enum GameGraphicsQuality
{
    Low,
    Medium,
    High,
}

public readonly record struct GameResolution(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";

    /// <summary>Common 16:9 and 16:10 render resolutions offered by the display menu, smallest first.</summary>
    public static IReadOnlyList<GameResolution> Common { get; } =
    [
        new(960, 600), new(1280, 720), new(1280, 800), new(1366, 768), new(1600, 900), new(1680, 1050),
        new(1920, 1080), new(1920, 1200), new(2560, 1440), new(2560, 1600), new(3840, 2160),
    ];
}

public sealed record GameDisplaySettings(
    int Width = 1280,
    int Height = 720,
    GameWindowMode Mode = GameWindowMode.Windowed,
    bool VSync = true,
    GameGraphicsQuality Quality = GameGraphicsQuality.High)
{
    [JsonIgnore]
    public GameResolution Resolution => new(Width, Height);

    public void Validate()
    {
        if (Width < 640 || Height < 480 || Width > 16384 || Height > 16384 || !Enum.IsDefined(Mode) || !Enum.IsDefined(Quality))
        {
            throw new ArgumentException("Display settings need a resolution of at least 640x480 and known window/quality modes.");
        }
    }

    /// <summary>Derive renderer settings from the game's authored look. High keeps it unchanged.</summary>
    public Graphics3DSettings ApplyQuality(Graphics3DSettings authored)
    {
        ArgumentNullException.ThrowIfNull(authored);
        return Quality switch
        {
            GameGraphicsQuality.High => authored,
            GameGraphicsQuality.Medium => authored with
            {
                AmbientOcclusionStrength = 0,
                LocalShadowBudget = Math.Min(1, authored.LocalShadowBudget),
            },
            _ => authored with
            {
                AntiAliasing = AntiAliasing3D.None,
                Shadows = false,
                BloomIntensity = 0,
                AmbientOcclusionStrength = 0,
                LocalShadowBudget = 0,
            },
        };
    }
}

public sealed record GameAudioSettings(float Master = 1, float Music = 0.8f, float Effects = 1)
{
    public void Validate()
    {
        if (!Unit(Master) || !Unit(Music) || !Unit(Effects))
        {
            throw new ArgumentException("Audio volumes must be between 0 and 1.");
        }
    }

    private static bool Unit(float value) => float.IsFinite(value) && value >= 0 && value <= 1;
}

public sealed record GameControlSettings(
    float MouseSensitivity = 0.0025f,
    bool InvertY = false,
    IReadOnlyList<GameBindingOverride>? Bindings = null)
{
    public const float MinimumSensitivity = 0.0005f;
    public const float MaximumSensitivity = 0.01f;

    public void Validate()
    {
        if (!float.IsFinite(MouseSensitivity) || MouseSensitivity < MinimumSensitivity || MouseSensitivity > MaximumSensitivity)
        {
            throw new ArgumentException($"Mouse sensitivity must be between {MinimumSensitivity} and {MaximumSensitivity}.");
        }
        if (Bindings is not null && Bindings.Any(binding => binding is null || binding.Control is null))
        {
            throw new ArgumentException("Saved bindings must name an action and a control.");
        }
    }
}

/// <summary>
/// Player preferences owned by the shell, not by any game: display, audio and controls.
/// They persist in a Deliverance preferences slot and never participate in world saves.
/// </summary>
public sealed record GameShellSettings(GameDisplaySettings Display, GameAudioSettings Audio, GameControlSettings Controls)
{
    public static GameShellSettings Default { get; } = new(new(), new(), new());

    public void Validate()
    {
        if (Display is null || Audio is null || Controls is null)
        {
            throw new InvalidDataException("Shell settings are incomplete.");
        }
        Display.Validate();
        Audio.Validate();
        Controls.Validate();
    }
}
