using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

public readonly record struct WetGroundSample3D(float Height, float Wetness, float Exposure)
{
    public float WaterDepth(float waterLevel) => Math.Max(0, waterLevel - Height);

    public static WetGroundSample3D FromHeight(float height, float waterLevel, float rain, float exposure, float dampBand = .12f)
    {
        if (!float.IsFinite(height) || !float.IsFinite(waterLevel) || !float.IsFinite(dampBand) || dampBand <= 0
            || !float.IsFinite(rain) || rain < 0 || rain > 1 || !float.IsFinite(exposure) || exposure < 0 || exposure > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }
        float edge = Math.Clamp((height - waterLevel) / dampBand, 0, 1);
        float damp = 1 - edge * edge * (3 - 2 * edge);
        return new(height, Math.Max(rain * exposure, damp), exposure);
    }
}

/// <summary>Immutable authored ground snapshot in world XZ metres. Queries use the mesh's triangle interpolation.</summary>
public sealed class WetGroundField3D
{
    public Vector2 Origin { get; }
    public Vector2 Size { get; }
    public int Columns { get; }
    public int Rows { get; }
    public float WaterLevel { get; }
    public ImmutableArray<WetGroundSample3D> Samples { get; }

    public WetGroundField3D(Vector2 origin, Vector2 size, int columns, int rows, float waterLevel,
        IEnumerable<WetGroundSample3D> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || !float.IsFinite(size.X) || !float.IsFinite(size.Y)
            || size.X < .01f || size.Y < .01f || size.X > 1000 || size.Y > 1000
            || columns is < 2 or > 257 || rows is < 2 or > 257 || !float.IsFinite(waterLevel) || Math.Abs(waterLevel) > 10000
            || !float.IsFinite(origin.X + size.X) || !float.IsFinite(origin.Y + size.Y)
            || origin.X + size.X / (columns - 1) <= origin.X || origin.Y + size.Y / (rows - 1) <= origin.Y)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
        var copy = samples.Take(columns * rows + 1).ToImmutableArray();
        if (copy.Length != columns * rows || copy.Any(sample => !float.IsFinite(sample.Height) || Math.Abs(sample.Height) > 10000
            || !float.IsFinite(sample.Wetness) || sample.Wetness < 0 || sample.Wetness > 1
            || !float.IsFinite(sample.Exposure) || sample.Exposure < 0 || sample.Exposure > 1))
        {
            throw new ArgumentException("Ground samples must be a complete finite grid with unit wetness and exposure.", nameof(samples));
        }
        Origin = origin;
        Size = size;
        Columns = columns;
        Rows = rows;
        WaterLevel = waterLevel;
        Samples = copy;
    }

    public Vector2 Position(int column, int row)
    {
        if (column < 0 || column >= Columns || row < 0 || row >= Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }
        return Origin + Size * new Vector2((float)column / (Columns - 1), (float)row / (Rows - 1));
    }

    public WetGroundSample3D Sample(Vector2 position)
    {
        Vector2 uv = (position - Origin) / Size;
        if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y) || uv.X < 0 || uv.X > 1 || uv.Y < 0 || uv.Y > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position is outside the ground field.");
        }
        float x = uv.X * (Columns - 1);
        float y = uv.Y * (Rows - 1);
        int column = Math.Min((int)x, Columns - 2);
        int row = Math.Min((int)y, Rows - 2);
        float u = x - column;
        float v = y - row;
        var a = Samples[row * Columns + column];
        var d = Samples[(row + 1) * Columns + column + 1];
        WetGroundSample3D b;
        Vector3 weights;
        if (v >= u)
        {
            b = Samples[(row + 1) * Columns + column];
            weights = new(1 - v, v - u, u);
        }
        else
        {
            b = Samples[row * Columns + column + 1];
            weights = new(1 - u, u - v, v);
        }
        return new(a.Height * weights.X + b.Height * weights.Y + d.Height * weights.Z,
            a.Wetness * weights.X + b.Wetness * weights.Y + d.Wetness * weights.Z,
            a.Exposure * weights.X + b.Exposure * weights.Y + d.Exposure * weights.Z);
    }
}

/// <summary>Authored sheltered volume. Feathering blends its XZ boundary; no hidden geometry queries.</summary>
public sealed record RainShelter3D(Vector3 Minimum, Vector3 Maximum, float Feather = .5f)
{
    public float ShelterAt(Vector3 position)
    {
        if (!Finite(Minimum) || !Finite(Maximum) || !Finite(position) || !float.IsFinite(Feather) || Feather <= 0
            || Minimum.X >= Maximum.X || Minimum.Y >= Maximum.Y || Minimum.Z >= Maximum.Z)
        {
            throw new ArgumentException("Shelter requires finite ordered bounds and a positive feather.");
        }
        if (position.Y < Minimum.Y || position.Y > Maximum.Y)
        {
            return 0;
        }
        float edge = Math.Min(Math.Min(position.X - Minimum.X, Maximum.X - position.X),
            Math.Min(position.Z - Minimum.Z, Maximum.Z - position.Z));
        float t = Math.Clamp(edge / Feather, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
