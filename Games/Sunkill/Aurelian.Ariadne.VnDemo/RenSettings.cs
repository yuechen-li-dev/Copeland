using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Audio;

namespace Aurelian.Ariadne.VnDemo;

public enum RenSetting
{
    MasterVolume,
    MusicVolume,
    SfxVolume,
}

public sealed record RenSettings(
    float MasterVolume,
    float MusicVolume,
    float SfxVolume)
{
    public static RenSettings Default { get; } = new(0.80f, 0.65f, 0.80f);

    public RenSettings Normalize()
    {
        return this with
        {
            MasterVolume = ClampVolume(MasterVolume, Default.MasterVolume),
            MusicVolume = ClampVolume(MusicVolume, Default.MusicVolume),
            SfxVolume = ClampVolume(SfxVolume, Default.SfxVolume),
        };
    }

    public RenSettings Adjust(RenSetting setting, int direction)
    {
        int sign = Math.Sign(direction);
        return setting switch
        {
            RenSetting.MasterVolume => this with
            {
                MasterVolume = Math.Clamp(MasterVolume + (sign * 0.10f), 0f, 1f),
            },
            RenSetting.MusicVolume => this with
            {
                MusicVolume = Math.Clamp(MusicVolume + (sign * 0.10f), 0f, 1f),
            },
            RenSetting.SfxVolume => this with
            {
                SfxVolume = Math.Clamp(SfxVolume + (sign * 0.10f), 0f, 1f),
            },
            _ => this,
        };
    }

    private static float ClampVolume(float value, float fallback)
    {
        return float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : fallback;
    }
}

public sealed class RenSettingsStore
{
    private readonly string path;

    public RenSettingsStore(string path)
    {
        this.path = path;
    }

    public string Path => path;

    public RenSettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return RenSettings.Default;
            }

            string json = File.ReadAllText(path);
            RenSettings? settings = JsonSerializer.Deserialize(
                json,
                RenSettingsJsonContext.Default.RenSettings);
            return settings?.Normalize() ?? RenSettings.Default;
        }
        catch (IOException)
        {
            return RenSettings.Default;
        }
        catch (JsonException)
        {
            return RenSettings.Default;
        }
    }

    public void Save(RenSettings settings)
    {
        string? directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(
            settings.Normalize(),
            RenSettingsJsonContext.Default.RenSettings);
        File.WriteAllText(path, json + Environment.NewLine);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(RenSettings))]
internal partial class RenSettingsJsonContext : JsonSerializerContext;

public sealed class RenAudioSettingsProjection : IDisposable
{
    private readonly AurelianAudioRuntime runtime;

    public RenAudioSettingsProjection()
    {
        runtime = new AurelianAudioRuntime(
            new AudioResourceScope(),
            new NullAudioOutputBackend());
    }

    public AudioRuntimeFacts Facts => runtime.Inspect();

    public void Apply(RenSettings settings)
    {
        RenSettings normalized = settings.Normalize();
        runtime.SetBusVolume(AudioBusId.Master, normalized.MasterVolume);
        runtime.SetBusVolume(AudioBusId.Music, normalized.MusicVolume);
        runtime.SetBusVolume(AudioBusId.Sfx, normalized.SfxVolume);
    }

    public void Dispose()
    {
        runtime.Dispose();
    }
}
