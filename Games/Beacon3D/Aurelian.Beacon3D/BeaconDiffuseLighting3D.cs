using System.Numerics;
using Aurelian.Games;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime.Rendering;

namespace Aurelian.Beacon3D;

/// <summary>Explicit whole-arena research profile shared by play and native qualification.</summary>
public static class BeaconDiffuseLighting3D
{
    public static DiffuseProbeGrid Grid { get; } = new(new(-10, .25f, -10), new(10, 5.5f, 10), 12, 3, 12, 128);
    public static DiffuseRegionPartition3D Partition { get; } = new(4, 1, 4);
    public static EnvironmentRadiance3D Source { get; } = new(1, 1, [.12f, .18f, .3f, 1]);
    public static Graphics3DSettings Settings { get; } = Graphics3DSettings.Default with
    {
        DirectDiffuseModel = DirectDiffuseModel3D.Lambert,
        SkyAmbient = Vector3.Zero,
        GroundAmbient = Vector3.Zero,
        EnvironmentIntensity = 1,
        AmbientOcclusionStrength = 0,
        BloomIntensity = 0,
        AntiAliasing = AntiAliasing3D.None,
        ShadowRadius = 18,
        ShadowDistance = 60,
        ShadowWorldBias = .002f,
        ShadowBias = .0001f,
        LocalShadowBudget = 256,
    };
    public static IReadOnlyList<LocalLight3D> Lights { get; } = Array.Empty<LocalLight3D>();
    public static DynamicDiffuseLighting3D Runtime { get; } = new()
    {
        Mode = DynamicDiffuseMode3D.Runtime,
        Grid = Grid,
        RaysPerProbePerFrame = 128,
    };

    public static NativeDiffuseRegionalPolicy3D Policy() => new(Grid, Partition, new DiffuseRegionalPolicySettings
    {
        MaximumRayUpdates = Grid.Count * 32,
    });
}
