using System.Collections.Immutable;
using System.Numerics;
using Aetheris.Cloth3D;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Agents;
using Aurelian.World.Scenes;

namespace Aurelian.Cloth3D;

/// <summary>The typed agent snapshot owns cloth state. Commands and stepping are explicit;
/// rendering never advances simulation. GPU callers may publish Capture() into the same state.</summary>
public sealed record ClothAgentDefinition3D(CompiledCloth3D Cloth)
    : AgentDefinition<ClothSnapshot3D>(AgentTemplate.Object("cloth"))
{
    public ClothSnapshot3D? InitialState { get; init; }
    public ClothPresentationBinding3D? Presentation { get; init; }

    public ClothAgentDefinition3D WithPresentation(int levels = 2)
    {
        return this with { Presentation = ClothPresentationBinding3D.Compile(Cloth, levels) };
    }

    public static ClothAgentDefinition3D FromGarment(ClothGarmentArtifact3D garment)
    {
        ArgumentNullException.ThrowIfNull(garment);
        using var solver = garment.CreateSolver();
        var source = solver.Capture();
        // Firmament artifacts use Z up; Aurelian scenes use Y up.
        return new(garment.Cloth)
        {
            InitialState = source with
            {
                Positions = source.Positions.Select(ToWorldAxes).ToImmutableArray(),
                Velocities = source.Velocities.Select(ToWorldAxes).ToImmutableArray(),
                PinTargets = source.PinTargets.Select(ToWorldAxes).ToImmutableArray(),
            },
        };

        static Vector3 ToWorldAxes(Vector3 value) => new(value.X, value.Z, -value.Y);
    }

    public ModelMaterial Material { get; init; } = new("cloth")
    {
        BaseColor = new(.035f, .35f, .55f, 1), Roughness = .9f, DoubleSided = true,
    };

    public override string Identity
    {
        get
        {
            if (Presentation is null)
            {
                return Cloth.ContentKey;
            }
            return Cloth.ContentKey + "/presentation-loop-v1/" + Presentation.Levels;
        }
    }

    public override void ValidatePlacement(ScenePlacement placement)
    {
        ValidateRigid(placement.WorldTransform);
        if (Presentation is not null && Presentation.SourceContentKey != Cloth.ContentKey)
        {
            throw new ArgumentException("Cloth presentation belongs to another simulation topology.");
        }
    }

    public override ClothSnapshot3D CreateState(ScenePlacement placement)
    {
        ClothSnapshot3D initial;
        if (InitialState is not null)
        {
            ClothState3D.ValidateSnapshot(Cloth, InitialState);
            initial = InitialState;
        }
        else
        {
            using var solver = new ClothSolver3D(Cloth);
            initial = solver.Capture();
        }
        return initial with
        {
            Positions = initial.Positions.Select(position =>
                Vector3.Transform(position, placement.WorldTransform)).ToImmutableArray(),
            Velocities = initial.Velocities.Select(velocity =>
                Vector3.TransformNormal(velocity, placement.WorldTransform)).ToImmutableArray(),
            PinTargets = initial.PinTargets.Select(position =>
                Vector3.Transform(position, placement.WorldTransform)).ToImmutableArray(),
        };
    }

    public void Advance(SceneAgent<ClothSnapshot3D> agent, ClothStepOptions3D options, ClothContacts3D contacts,
        IReadOnlyDictionary<int, Vector3>? pinTargets = null)
    {
        if (!ReferenceEquals(agent.Definition, this))
        {
            throw new ArgumentException("Cloth agent belongs to another definition.");
        }
        using var solver = new ClothSolver3D(Cloth);
        solver.Restore(agent.State);
        if (pinTargets is not null)
        {
            foreach (var (vertex, target) in pinTargets)
            {
                solver.SetPin(vertex, target);
            }
        }
        solver.Step(options, contacts);
        agent.State = solver.Capture();
    }

    public override Matrix4x4 WorldTransform(ClothSnapshot3D state, ScenePlacement placement) => Matrix4x4.Identity;

    public override SceneGroup Present(ClothSnapshot3D state)
    {
        return Scene.Group("cloth-body", [ClothPresentation3D.Mesh(Cloth, state, Material, Presentation)]);
    }

    /// <summary>Cloth is simulated in world space, so its placement may move and rotate it but not scale or shear it.</summary>
    private static void ValidateRigid(Matrix4x4 matrix)
    {
        SceneTransform.ValidateMatrix(matrix);
        Vector3 x = new(matrix.M11, matrix.M12, matrix.M13);
        Vector3 y = new(matrix.M21, matrix.M22, matrix.M23);
        Vector3 z = new(matrix.M31, matrix.M32, matrix.M33);
        bool unitAxes = MathF.Abs(x.LengthSquared() - 1) <= .0001f && MathF.Abs(y.LengthSquared() - 1) <= .0001f
            && MathF.Abs(z.LengthSquared() - 1) <= .0001f;
        bool orthogonal = MathF.Abs(Vector3.Dot(x, y)) <= .0001f && MathF.Abs(Vector3.Dot(x, z)) <= .0001f
            && MathF.Abs(Vector3.Dot(y, z)) <= .0001f;
        if (!unitAxes || !orthogonal)
        {
            throw new NotSupportedException("AUR-PHYSICS-002: Physics agents require rigid placement; author dimensions in the shape and visual.");
        }
    }
}

public static class ClothPresentation3D
{
    public static SceneMesh Mesh(CompiledCloth3D plan, ClothSnapshot3D snapshot, ModelMaterial material,
        ClothPresentationBinding3D? presentation = null)
    {
        ClothState3D.ValidateSnapshot(plan, snapshot);
        var positions = snapshot.Positions;
        var indices = plan.Definition.Indices;
        var coordinates = plan.Definition.Coordinates;
        string identity = plan.ContentKey;
        if (presentation is not null)
        {
            if (presentation.SourceContentKey != plan.ContentKey)
            {
                throw new ArgumentException("Cloth presentation belongs to another simulation topology.");
            }
            var refined = presentation.Evaluate(snapshot);
            positions = refined.Positions;
            indices = refined.Indices;
            coordinates = presentation.Coordinates;
            identity += "/presentation-loop-v1/" + presentation.Levels;
        }
        var normals = new Vector3[positions.Length];
        for (int triangle = 0; triangle < indices.Length; triangle += 3)
        {
            int a = indices[triangle];
            int b = indices[triangle + 1];
            int c = indices[triangle + 2];
            Vector3 normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }
        var vertices = ImmutableArray.CreateBuilder<SceneVertex>(normals.Length);
        for (int vertex = 0; vertex < normals.Length; vertex++)
        {
            Vector3 normal = normals[vertex].LengthSquared() > 1e-12f ? Vector3.Normalize(normals[vertex]) : Vector3.UnitY;
            Vector2 uv = coordinates[vertex];
            bool stripe = ((int)MathF.Floor(uv.X * 6) + (int)MathF.Floor(uv.Y * 6)) % 2 == 0;
            Vector4 color = stripe ? Vector4.One : new(.65f, .75f, .8f, 1);
            vertices.Add(new(positions[vertex], normal, color));
        }
        return new("cloth", vertices.MoveToImmutable())
        {
            Indices = indices,
            SourceIdentity = identity,
            Material = material,
        };
    }

}
