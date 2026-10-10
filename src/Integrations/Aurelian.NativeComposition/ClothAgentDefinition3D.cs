using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Cloth3D;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Agents;
using Aurelian.World.Scenes;

namespace Aurelian.NativeComposition;

/// <summary>The typed agent snapshot owns cloth state. Commands and stepping are explicit;
/// rendering never advances simulation. GPU callers may publish Capture() into the same state.</summary>
public sealed record ClothAgentDefinition3D(CompiledCloth3D Cloth)
    : AgentDefinition<ClothSnapshot3D>(AgentTemplate.Object("cloth"))
{
    public ModelMaterial Material { get; init; } = new("cloth")
    {
        BaseColor = new(.035f, .35f, .55f, 1), Roughness = .9f, DoubleSided = true,
    };

    public override string Identity => Cloth.ContentKey;

    public override void ValidatePlacement(ScenePlacement placement)
    {
        PhysicsRigidPlacement3D.From(placement.WorldTransform);
    }

    public override ClothSnapshot3D CreateState(ScenePlacement placement)
    {
        var positions = Cloth.Definition.Positions.Select(position => Vector3.Transform(position, placement.WorldTransform)).ToImmutableArray();
        return new(Cloth.ContentKey, 0, positions,
            Enumerable.Repeat(Vector3.Zero, positions.Length).ToImmutableArray(), positions);
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

    public override SceneGroup Present(ClothSnapshot3D state) => Scene.Group("cloth-body", [ClothPresentation3D.Mesh(Cloth, state, Material)]);
}

public static class ClothPresentation3D
{
    public static SceneMesh Mesh(CompiledCloth3D plan, ClothSnapshot3D snapshot, ModelMaterial material)
    {
        ClothState3D.ValidateSnapshot(plan, snapshot);
        var normals = new Vector3[snapshot.Positions.Length];
        var indices = plan.Definition.Indices;
        for (int triangle = 0; triangle < indices.Length; triangle += 3)
        {
            int a = indices[triangle];
            int b = indices[triangle + 1];
            int c = indices[triangle + 2];
            Vector3 normal = Vector3.Cross(snapshot.Positions[b] - snapshot.Positions[a], snapshot.Positions[c] - snapshot.Positions[a]);
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }
        var vertices = ImmutableArray.CreateBuilder<SceneVertex>(normals.Length);
        for (int vertex = 0; vertex < normals.Length; vertex++)
        {
            Vector3 normal = normals[vertex].LengthSquared() > 1e-12f ? Vector3.Normalize(normals[vertex]) : Vector3.UnitY;
            Vector2 uv = plan.Definition.Coordinates[vertex];
            bool stripe = ((int)MathF.Floor(uv.X * 6) + (int)MathF.Floor(uv.Y * 6)) % 2 == 0;
            Vector4 color = stripe ? Vector4.One : new(.65f, .75f, .8f, 1);
            vertices.Add(new(snapshot.Positions[vertex], normal, color));
        }
        return new("cloth", vertices.MoveToImmutable())
        {
            Indices = indices,
            SourceIdentity = plan.ContentKey,
            Material = material,
        };
    }
}
