using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Cloth3D;

public readonly record struct ClothDispatch3D(int Phase, int Start, int Count, bool ResetLambda = false);

/// <summary>Version 1 scalar storage ABI: 16 floats/vertex, then 3 multiplier floats/constraint;
/// 12 floats/source stencil; 32 floats/dispatch header. All integer values are exact f32 integers.</summary>
public sealed class ClothGpuData3D
{
    public CompiledCloth3D Plan { get; }
    public float[] Source { get; }
    public ImmutableArray<ClothDispatch3D> Constraints { get; }
    public ImmutableArray<ClothDispatch3D> Triangles { get; }

    public ClothGpuData3D(CompiledCloth3D plan)
    {
        Plan = plan;
        Source = new float[(plan.Constraints.Length + plan.Definition.Indices.Length / 3) * 12];
        var constraints = ImmutableArray.CreateBuilder<ClothDispatch3D>();
        var triangles = ImmutableArray.CreateBuilder<ClothDispatch3D>();
        int index = 0;
        foreach (var color in plan.ConstraintColors)
        {
            constraints.Add(new(1, index, color.Length));
            foreach (int original in color)
            {
                var constraint = plan.Constraints[original];
                int offset = index++ * 12;
                Source[offset] = (int)constraint.Kind;
                for (int local = 0; local < constraint.Vertices.Length; local++)
                {
                    Source[offset + 1 + local] = constraint.Vertices[local];
                }
                Source[offset + 5] = constraint.RestLength;
                Source[offset + 6] = constraint.Compliance;
                for (int local = 0; local < constraint.Weights.Length; local++)
                {
                    Source[offset + 7 + local] = constraint.Weights[local];
                }
            }
        }
        foreach (var color in plan.TriangleColors)
        {
            triangles.Add(new(3, index, color.Length));
            foreach (int triangle in color)
            {
                int offset = index++ * 12;
                Source[offset] = 4;
                for (int local = 0; local < 3; local++)
                {
                    Source[offset + 1 + local] = plan.Definition.Indices[triangle * 3 + local];
                }
            }
        }
        Constraints = constraints.ToImmutable();
        Triangles = triangles.ToImmutable();
    }

    public float[] State(ClothSnapshot3D snapshot)
    {
        ClothState3D.ValidateSnapshot(Plan, snapshot);
        float[] state = new float[Plan.Definition.Positions.Length * 16 + Plan.Constraints.Length * 3];
        for (int vertex = 0; vertex < snapshot.Positions.Length; vertex++)
        {
            int offset = vertex * 16;
            WriteVector(state, offset, snapshot.Positions[vertex]);
            WriteVector(state, offset + 6, snapshot.Velocities[vertex]);
            state[offset + 9] = Plan.InverseMasses[vertex];
            WriteVector(state, offset + 10, snapshot.PinTargets[vertex]);
        }
        return state;
    }

    public ClothSnapshot3D Snapshot(ReadOnlySpan<float> state, long tick)
    {
        int count = Plan.Definition.Positions.Length;
        if (state.Length < count * 16)
        {
            throw new ArgumentException("Truncated cloth GPU state.");
        }
        var positions = ImmutableArray.CreateBuilder<Vector3>(count);
        var velocities = ImmutableArray.CreateBuilder<Vector3>(count);
        var targets = ImmutableArray.CreateBuilder<Vector3>(count);
        for (int vertex = 0; vertex < count; vertex++)
        {
            positions.Add(ReadVector(state, vertex * 16));
            velocities.Add(ReadVector(state, vertex * 16 + 6));
            targets.Add(ReadVector(state, vertex * 16 + 10));
        }
        var snapshot = new ClothSnapshot3D(Plan.ContentKey, tick, positions.MoveToImmutable(),
            velocities.MoveToImmutable(), targets.MoveToImmutable());
        ClothState3D.ValidateSnapshot(Plan, snapshot);
        return snapshot;
    }

    public float[] Header(ClothStepOptions3D options, ClothContacts3D contacts, ClothDispatch3D dispatch, float fraction)
    {
        float h = options.DeltaSeconds / options.Substeps;
        float[] header = new float[32];
        header[0] = dispatch.Phase;
        header[1] = dispatch.Count;
        header[2] = dispatch.Start;
        header[3] = Plan.Definition.Positions.Length;
        header[4] = h;
        WriteVector(header, 5, options.Gravity);
        header[8] = MathF.Exp(-options.VelocityDamping * h);
        header[9] = dispatch.ResetLambda ? 1 : 0;
        header[10] = fraction;
        header[11] = Plan.Definition.Material.Thickness * .5f;
        if (contacts.PlaneNormal is { } normal)
        {
            header[12] = 1;
            WriteVector(header, 13, normal);
            header[16] = contacts.PlaneOffset;
        }
        if (contacts.SphereCenter is { } sphere)
        {
            header[17] = 1;
            WriteVector(header, 18, sphere);
            header[21] = contacts.SphereRadius;
        }
        return header;
    }

    private static Vector3 ReadVector(ReadOnlySpan<float> values, int offset) => new(values[offset], values[offset + 1], values[offset + 2]);
    private static void WriteVector(float[] values, int offset, Vector3 vector)
    {
        values[offset] = vector.X;
        values[offset + 1] = vector.Y;
        values[offset + 2] = vector.Z;
    }
}
