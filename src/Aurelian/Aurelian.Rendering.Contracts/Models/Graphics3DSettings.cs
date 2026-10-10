using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

/// <summary>Scene lighting and output policy. Colors and intensities are linear; exposure is a multiplier.</summary>
public enum AntiAliasing3D
{
    None,
    Temporal,
}

public enum SurfaceDebugView3D
{
    Shaded,
    AmbientOcclusion,
}

public sealed record Graphics3DSettings
{
    public static Graphics3DSettings Default { get; } = new();
    public static Graphics3DSettings Basic { get; } = new() { SolidPbr = false, Shadows = false, ToneMapping = false, AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0 };

    public AntiAliasing3D AntiAliasing { get; init; } = AntiAliasing3D.Temporal;
    public float TemporalHistoryWeight { get; init; } = .875f;
    public float BloomIntensity { get; init; } = .08f;
    public float BloomThreshold { get; init; } = 1;
    public float BloomKnee { get; init; } = .5f;
    public float AmbientOcclusionStrength { get; init; }
    public float AmbientOcclusionRadius { get; init; } = .8f;
    public SurfaceDebugView3D SurfaceDebugView { get; init; }
    public float EnvironmentIntensity { get; init; } = 1;
    public bool LocalLightCulling { get; init; } = true;
    public int LocalShadowBudget { get; init; } = 2;
    public bool SolidPbr { get; init; } = true;
    public bool Shadows { get; init; } = true;
    public bool ToneMapping { get; init; } = true;
    public Vector3 SunDirection { get; init; } = Vector3.Normalize(new Vector3(.65f, .75f, -.45f));
    public Vector3 SunColor { get; init; } = new(1, .91f, .78f);
    public float SunIntensity { get; init; } = 3.5f;
    public Vector3 SkyAmbient { get; init; } = new(.22f, .30f, .43f);
    public Vector3 GroundAmbient { get; init; } = new(.07f, .055f, .04f);
    public float Exposure { get; init; } = 1;
    public float SolidRoughness { get; init; } = .65f;
    public float SolidMetallic { get; init; }
    public float ShadowRadius { get; init; } = 24;
    public float ShadowBias { get; init; } = .0008f;
    public float ShadowDistance { get; init; } = 100;
    public float ShadowCasterPadding { get; init; } = 30;
    public float ShadowWorldBias { get; init; } = .015f;
    public HeightFog3D Fog { get; init; } = new();
    public VolumetricLighting3D Volumetrics { get; init; } = new();
    public float RefractionTraceDistance { get; init; } = 64;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Fog);
        Fog.Validate();
        ArgumentNullException.ThrowIfNull(Volumetrics);
        Volumetrics.Validate();
        if (!Enum.IsDefined(AntiAliasing) || !Enum.IsDefined(SurfaceDebugView)
            || !Range(TemporalHistoryWeight, 0, .95f) || !Range(BloomIntensity, 0, 1)
            || !Range(AmbientOcclusionStrength, 0, 2) || !Range(AmbientOcclusionRadius, .01f, 10)
            || !Range(EnvironmentIntensity, 0, 100)
            || !Range(RefractionTraceDistance, 1, 1000)
            || LocalShadowBudget is < 0 or > 2
            || !Range(BloomThreshold, 0, 10000) || !Range(BloomKnee, 0, 10000)
            || !Finite(SunDirection) || !float.IsFinite(SunDirection.LengthSquared()) || SunDirection.LengthSquared() < .000001f
            || !Color(SunColor) || !Color(SkyAmbient) || !Color(GroundAmbient)
            || !Range(SunIntensity, 0, 100_000) || !Range(Exposure, .001f, 1_000)
            || !Range(SolidRoughness, .045f, 1) || !Range(SolidMetallic, 0, 1)
            || !Range(ShadowDistance, 1, 10000) || !Range(ShadowCasterPadding, 1, 1000)
            || !Range(ShadowWorldBias, 0, 1) || !Range(ShadowRadius, 1, 1_000) || !Range(ShadowBias, 0, .05f))
        {
            throw new ArgumentOutOfRangeException(nameof(Graphics3DSettings), "Lighting, exposure and shadow settings must be finite and within their supported ranges.");
        }
    }

    private static bool Range(float value, float minimum, float maximum) => float.IsFinite(value) && value >= minimum && value <= maximum;
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Color(Vector3 value) => Finite(value) && value.X >= 0 && value.Y >= 0 && value.Z >= 0;
}
