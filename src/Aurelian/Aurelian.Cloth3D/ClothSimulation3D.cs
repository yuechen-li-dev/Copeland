using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Spatial3D;

namespace Aurelian.Cloth3D;

public sealed record ClothStepOptions3D
{
    public float DeltaSeconds { get; init; } = 1f / 60;
    public int Substeps { get; init; } = 8;
    public int Iterations { get; init; } = 4;
    public int ContactIterations { get; init; } = 2;
    public Vector3 Gravity { get; init; } = new(0, -9.81f, 0);
    public float VelocityDamping { get; init; } = 1.5f;

    public void Validate()
    {
        ClothValidation3D.Finite(Gravity);
        if (!float.IsFinite(DeltaSeconds) || DeltaSeconds is < .00001f or > .1f
            || Substeps is < 1 or > 64 || Iterations is < 1 or > 64 || ContactIterations is < 1 or > 32
            || !float.IsFinite(VelocityDamping) || VelocityDamping < 0)
        {
            throw new ArgumentException("Invalid bounded cloth step options.");
        }
    }
}

/// <summary>One-way, discrete contact baseline. Plane equation dot(normal,p) >= offset;
/// sphere contact covers vertices AND triangle interiors. No CCD or self-contact claim.</summary>
public sealed record ClothContacts3D
{
    public Vector3? PlaneNormal { get; init; }
    public float PlaneOffset { get; init; }
    public Vector3? SphereCenter { get; init; }
    public float SphereRadius { get; init; }

    public void Validate()
    {
        if (PlaneNormal is { } normal)
        {
            ClothValidation3D.Finite(normal);
            if (MathF.Abs(normal.LengthSquared() - 1) > .0001f)
            {
                throw new ArgumentException("Cloth plane requires a unit normal.");
            }
        }
        if (SphereCenter is { } center)
        {
            ClothValidation3D.Finite(center);
            if (!float.IsFinite(SphereRadius) || SphereRadius <= 0)
            {
                throw new ArgumentException("Cloth sphere radius must be positive.");
            }
        }
        if (!float.IsFinite(PlaneOffset))
        {
            throw new ArgumentException("Nonfinite plane offset.");
        }
    }
}

public sealed record ClothSnapshot3D(string ContentKey, long Tick, ImmutableArray<Vector3> Positions,
    ImmutableArray<Vector3> Velocities, ImmutableArray<Vector3> PinTargets);

public sealed record ClothMetrics3D(float MaximumStretch, float MaximumPinError,
    float MaximumSurfacePenetration, float BendEnergy, bool Finite);

public interface IClothSolver3D : IDisposable
{
    CompiledCloth3D Plan { get; }
    long Tick { get; }
    void SetPin(int vertex, Vector3 target);
    void Step(ClothStepOptions3D options, ClothContacts3D contacts);
    ClothSnapshot3D Capture();
    void Restore(ClothSnapshot3D snapshot);
}

/// <summary>Deterministic CPU reference for the same colored schedule used by Vulkan.
/// Multipliers reset per substep, accumulate across iterations, and are not warm-started.</summary>
public sealed class ClothSolver3D : IClothSolver3D
{
    private readonly Vector3[] positions;
    private readonly Vector3[] velocities;
    private readonly Vector3[] previous;
    private readonly Vector3[] targets;
    private readonly Vector3[] pinStarts;
    private readonly Vector3[] lambda;

    public CompiledCloth3D Plan { get; }
    public long Tick { get; private set; }

    public ClothSolver3D(CompiledCloth3D plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;
        positions = plan.Definition.Positions.ToArray();
        previous = new Vector3[positions.Length];
        velocities = new Vector3[positions.Length];
        targets = positions.ToArray();
        pinStarts = new Vector3[positions.Length];
        lambda = new Vector3[plan.Constraints.Length];
    }

    public void SetPin(int vertex, Vector3 target)
    {
        ClothState3D.ValidatePin(Plan, vertex, target);
        targets[vertex] = target;
    }

    public void Step(ClothStepOptions3D options, ClothContacts3D contacts)
    {
        ClothState3D.ValidateStep(Plan, options, contacts);
        float h = options.DeltaSeconds / options.Substeps;
        float damping = MathF.Exp(-options.VelocityDamping * h);
        positions.CopyTo(pinStarts, 0);
        for (int substep = 0; substep < options.Substeps; substep++)
        {
            Array.Clear(lambda);
            float fraction = (substep + 1f) / options.Substeps;
            for (int vertex = 0; vertex < positions.Length; vertex++)
            {
                previous[vertex] = positions[vertex];
                if (Plan.InverseMasses[vertex] == 0)
                {
                    positions[vertex] = Vector3.Lerp(pinStarts[vertex], targets[vertex], fraction);
                }
                else
                {
                    velocities[vertex] += options.Gravity * h;
                    positions[vertex] += velocities[vertex] * h;
                }
            }
            for (int iteration = 0; iteration < options.Iterations; iteration++)
            {
                foreach (var color in Plan.ConstraintColors)
                {
                    foreach (int index in color)
                    {
                        ProjectConstraint(index, h);
                    }
                }
                for (int contact = 0; contact < options.ContactIterations; contact++)
                {
                    ProjectContacts(contacts);
                }
            }
            for (int vertex = 0; vertex < positions.Length; vertex++)
            {
                velocities[vertex] = (positions[vertex] - previous[vertex]) * (damping / h);
            }
        }
        Tick++;
    }

    private void ProjectConstraint(int index, float h)
    {
        var constraint = Plan.Constraints[index];
        float alpha = constraint.Compliance / (h * h);
        if (constraint.Kind != ClothConstraintKind3D.FlatBend)
        {
            int a = constraint.Vertices[0];
            int b = constraint.Vertices[1];
            float wa = Plan.InverseMasses[a];
            float wb = Plan.InverseMasses[b];
            Vector3 difference = positions[a] - positions[b];
            float length = difference.Length();
            if (length < 1e-10f || wa + wb == 0)
            {
                return;
            }
            float delta = (-(length - constraint.RestLength) - alpha * lambda[index].X) / (wa + wb + alpha);
            lambda[index].X += delta;
            Vector3 correction = difference * (delta / length);
            positions[a] += correction * wa;
            positions[b] -= correction * wb;
            return;
        }
        Vector3 curvature = Vector3.Zero;
        float denominator = alpha;
        float movable = 0;
        for (int local = 0; local < 4; local++)
        {
            int vertex = constraint.Vertices[local];
            float weight = constraint.Weights[local];
            curvature += positions[vertex] * weight;
            float term = Plan.InverseMasses[vertex] * weight * weight;
            denominator += term;
            movable += term;
        }
        if (movable <= 1e-20f)
        {
            return;
        }
        Vector3 increment = (-curvature - alpha * lambda[index]) / denominator;
        lambda[index] += increment;
        for (int local = 0; local < 4; local++)
        {
            int vertex = constraint.Vertices[local];
            positions[vertex] += increment * (Plan.InverseMasses[vertex] * constraint.Weights[local]);
        }
    }

    private void ProjectContacts(ClothContacts3D contacts)
    {
        float thickness = Plan.Definition.Material.Thickness * .5f;
        for (int vertex = 0; vertex < positions.Length; vertex++)
        {
            if (Plan.InverseMasses[vertex] == 0)
            {
                continue;
            }
            if (contacts.PlaneNormal is { } normal)
            {
                float gap = Vector3.Dot(normal, positions[vertex]) - contacts.PlaneOffset - thickness;
                if (gap < 0)
                {
                    positions[vertex] -= normal * gap;
                }
            }
            if (contacts.SphereCenter is { } center)
            {
                Vector3 delta = positions[vertex] - center;
                float distance = delta.Length();
                float radius = contacts.SphereRadius + thickness;
                if (distance < radius)
                {
                    Vector3 outward = distance > 1e-10f ? delta / distance : Vector3.UnitY;
                    positions[vertex] = center + outward * radius;
                }
            }
        }
        if (contacts.SphereCenter is not { } sphere)
        {
            return;
        }
        foreach (var color in Plan.TriangleColors)
        {
            foreach (int triangle in color)
            {
                int a = Plan.Definition.Indices[triangle * 3];
                int b = Plan.Definition.Indices[triangle * 3 + 1];
                int c = Plan.Definition.Indices[triangle * 3 + 2];
                var closest = TriangleSurface3D.ClosestPoint(sphere, positions[a], positions[b], positions[c]);
                Vector3 delta = closest.Point - sphere;
                float distance = delta.Length();
                float penetration = contacts.SphereRadius + thickness - distance;
                if (penetration <= 0)
                {
                    continue;
                }
                Vector3 barycentric = closest.Barycentric;
                float denominator = Plan.InverseMasses[a] * barycentric.X * barycentric.X
                    + Plan.InverseMasses[b] * barycentric.Y * barycentric.Y
                    + Plan.InverseMasses[c] * barycentric.Z * barycentric.Z;
                if (denominator <= 1e-20f)
                {
                    continue;
                }
                Vector3 normal = distance > 1e-10f ? delta / distance : Vector3.UnitY;
                Vector3 correction = normal * (penetration / denominator);
                positions[a] += correction * (Plan.InverseMasses[a] * barycentric.X);
                positions[b] += correction * (Plan.InverseMasses[b] * barycentric.Y);
                positions[c] += correction * (Plan.InverseMasses[c] * barycentric.Z);
            }
        }
    }

    public ClothSnapshot3D Capture() => new(Plan.ContentKey, Tick, positions.ToImmutableArray(),
        velocities.ToImmutableArray(), targets.ToImmutableArray());

    public void Restore(ClothSnapshot3D snapshot)
    {
        ClothState3D.ValidateSnapshot(Plan, snapshot);
        snapshot.Positions.CopyTo(positions);
        snapshot.Velocities.CopyTo(velocities);
        snapshot.PinTargets.CopyTo(targets);
        Tick = snapshot.Tick;
    }

    public void Dispose() { }
}

public static class ClothState3D
{
    public static void ValidateStep(CompiledCloth3D plan, ClothStepOptions3D options, ClothContacts3D contacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(contacts);
        options.Validate();
        contacts.Validate();
        float h = options.DeltaSeconds / options.Substeps;
        if (plan.Constraints.Any(constraint => !float.IsFinite(constraint.Compliance / (h * h))))
        {
            throw new ArgumentException("Cloth compliance and timestep exceed finite solver arithmetic.");
        }
    }

    public static void ValidatePin(CompiledCloth3D plan, int vertex, Vector3 target)
    {
        ClothValidation3D.Finite(target);
        if (vertex < 0 || vertex >= plan.InverseMasses.Length || plan.InverseMasses[vertex] != 0)
        {
            throw new ArgumentException("Pin commands require an authored pinned vertex.");
        }
    }

    public static void ValidateSnapshot(CompiledCloth3D plan, ClothSnapshot3D snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        int count = plan.Definition.Positions.Length;
        if (snapshot.ContentKey != plan.ContentKey || snapshot.Tick < 0 || snapshot.Positions.IsDefault
            || snapshot.Velocities.IsDefault || snapshot.PinTargets.IsDefault || snapshot.Positions.Length != count
            || snapshot.Velocities.Length != count || snapshot.PinTargets.Length != count)
        {
            throw new ArgumentException("Cloth snapshot belongs to another topology/material or has invalid state dimensions.");
        }
        foreach (Vector3 value in snapshot.Positions)
        {
            ValidateVector(value);
        }
        foreach (Vector3 value in snapshot.Velocities)
        {
            ValidateVector(value);
        }
        foreach (Vector3 value in snapshot.PinTargets)
        {
            ValidateVector(value);
        }
    }

    private static void ValidateVector(Vector3 value) => ClothValidation3D.Finite(value);

    public static ClothMetrics3D Measure(CompiledCloth3D plan, ClothSnapshot3D snapshot, ClothContacts3D contacts)
    {
        ValidateSnapshot(plan, snapshot);
        contacts.Validate();
        var positions = snapshot.Positions;
        float stretch = 0;
        float bending = 0;
        foreach (var constraint in plan.Constraints)
        {
            if (constraint.Kind != ClothConstraintKind3D.FlatBend)
            {
                float length = Vector3.Distance(positions[constraint.Vertices[0]], positions[constraint.Vertices[1]]);
                stretch = MathF.Max(stretch, length / constraint.RestLength - 1);
            }
            else
            {
                Vector3 curvature = Vector3.Zero;
                for (int local = 0; local < 4; local++)
                {
                    curvature += positions[constraint.Vertices[local]] * constraint.Weights[local];
                }
                bending += .5f * curvature.LengthSquared();
            }
        }
        float pinError = 0;
        foreach (int vertex in plan.Definition.Pins)
        {
            pinError = MathF.Max(pinError, Vector3.Distance(positions[vertex], snapshot.PinTargets[vertex]));
        }
        float penetration = 0;
        float halfThickness = plan.Definition.Material.Thickness * .5f;
        if (contacts.PlaneNormal is { } normal)
        {
            foreach (Vector3 position in positions)
            {
                penetration = MathF.Max(penetration, contacts.PlaneOffset + halfThickness - Vector3.Dot(normal, position));
            }
        }
        if (contacts.SphereCenter is { } sphere)
        {
            for (int triangle = 0; triangle < plan.Definition.Indices.Length; triangle += 3)
            {
                var indices = plan.Definition.Indices;
                var closest = TriangleSurface3D.ClosestPoint(sphere, positions[indices[triangle]],
                    positions[indices[triangle + 1]], positions[indices[triangle + 2]]);
                penetration = MathF.Max(penetration, contacts.SphereRadius + halfThickness - Vector3.Distance(sphere, closest.Point));
            }
        }
        return new(stretch, pinError, penetration, bending, float.IsFinite(stretch) && float.IsFinite(bending));
    }
}
