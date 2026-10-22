using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

namespace Cloudstep;

/// <summary>Opt-in game-authored material calibration and engine settings. No simulation state.</summary>
public sealed record CloudstepLookProfile
{
    public Graphics3DSettings Graphics { get; init; } = Graphics3DSettings.Default;
    public bool DecodePaletteSrgb { get; init; }
    public Vector3 GrassTint { get; init; } = Vector3.One;
    public Vector3 EarthTint { get; init; } = Vector3.One;
    public Vector3 HeroTint { get; init; } = Vector3.One;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Graphics);
        Graphics.Validate();
        foreach (Vector3 tint in new[] { GrassTint, EarthTint, HeroTint })
        {
            if (!Valid(tint.X) || !Valid(tint.Y) || !Valid(tint.Z))
            {
                throw new InvalidDataException("Research palette multipliers must be finite and between 0.25 and 4.");
            }
        }
    }

    private static bool Valid(float value) => float.IsFinite(value) && value is >= .25f and <= 4;
}

public enum CloudstepLookRegion
{
    Background,
    Hero,
    Grass,
    Earth,
    Structure,
}

/// <summary>Transforms source palette values once per distinct color/mesh, retaining authored geometry and materials.</summary>
public sealed class CloudstepPalette
{
    private readonly CloudstepLookProfile profile;
    private readonly Dictionary<(Vector4 Color, CloudstepLookRegion Region), Vector4> colors = new();
    private readonly Dictionary<(ImmutableArray<SceneVertex> Vertices, CloudstepLookRegion Region), ImmutableArray<SceneVertex>> meshes = new();

    public CloudstepPalette(CloudstepLookProfile profile)
    {
        profile.Validate();
        this.profile = profile;
    }

    public static CloudstepLookRegion Region(string id, Vector4 color, ModelMaterial? material)
    {
        if (id == "cloud-sea" || material is not null)
        {
            return CloudstepLookRegion.Background;
        }
        if (id.StartsWith("hero.", StringComparison.Ordinal))
        {
            return CloudstepLookRegion.Hero;
        }
        if (color == CloudstepVisuals.Grass || color == CloudstepVisuals.GrassEdge)
        {
            return CloudstepLookRegion.Grass;
        }
        if (color == CloudstepVisuals.Earth)
        {
            return CloudstepLookRegion.Earth;
        }
        return CloudstepLookRegion.Structure;
    }

    public SceneFrame Apply(SceneFrame frame)
    {
        return frame with
        {
            Boxes = frame.Boxes.Select(box => box with
            {
                Color = Color(box.Color, Region(box.Id, box.Color, box.Material)),
            }).ToImmutableArray(),
            Meshes = frame.Meshes.Select(mesh => mesh with
            {
                Vertices = mesh.Vertices.IsEmpty
                    ? mesh.Vertices
                    : Vertices(mesh.Vertices, Region(mesh.Id, mesh.Vertices[0].Color, mesh.Material)),
            }).ToImmutableArray(),
        };
    }

    private ImmutableArray<SceneVertex> Vertices(ImmutableArray<SceneVertex> source, CloudstepLookRegion region)
    {
        if (region == CloudstepLookRegion.Background)
        {
            return source;
        }
        var key = (source, region);
        if (!meshes.TryGetValue(key, out var result))
        {
            result = source.Select(vertex => vertex with { Color = Color(vertex.Color, region) }).ToImmutableArray();
            meshes.Add(key, result);
        }
        return result;
    }

    private Vector4 Color(Vector4 source, CloudstepLookRegion region)
    {
        if (region == CloudstepLookRegion.Background)
        {
            return source;
        }
        var key = (source, region);
        if (colors.TryGetValue(key, out Vector4 cached))
        {
            return cached;
        }
        Vector3 tint = region switch
        {
            CloudstepLookRegion.Hero => profile.HeroTint,
            CloudstepLookRegion.Grass => profile.GrassTint,
            CloudstepLookRegion.Earth => profile.EarthTint,
            _ => Vector3.One,
        };
        float Channel(float value, float multiplier)
        {
            if (profile.DecodePaletteSrgb)
            {
                value = value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
            }
            return Math.Clamp(value * multiplier, 0, 1);
        }
        Vector4 result = new(Channel(source.X, tint.X), Channel(source.Y, tint.Y), Channel(source.Z, tint.Z), source.W);
        colors.Add(key, result);
        return result;
    }
}
