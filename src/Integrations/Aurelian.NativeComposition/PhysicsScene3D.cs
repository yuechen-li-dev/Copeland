using System.Numerics;
using Aurelian.Physics3D;
using Aurelian.Spatial3D;
using Aurelian.World.Agents;
using Aurelian.World.Scenes;

namespace Aurelian.NativeComposition;

/// <summary>Physics owns motion; the scene agent receives an explicit inspectable projection after Step.</summary>
public sealed record PhysicsAgentDefinition3D(IPhysicsWorld3D World, PhysicsBody3D Body,
    SceneGroup Visual, string DefinitionId) : AgentDefinition<PhysicsBodyState3D>(AgentTemplate.Object("physics-body"))
{
    public override string Identity => PhysicsAgentIdentity3D.Compute(this);

    public override void ValidatePlacement(ScenePlacement placement)
    {
        ArgumentNullException.ThrowIfNull(World);
        ArgumentNullException.ThrowIfNull(Visual);
        Description(placement).Validate();
    }

    public override PhysicsBodyState3D CreateState(ScenePlacement placement)
    {
        PhysicsBody3D description = Description(placement);
        return new(description.Id, description.SemanticOwnerId, description.MotionType,
            description.Pose, description.Velocity, description.MotionType != PhysicsMotionType3D.Static);
    }

    public override IDisposable Activate(SceneAgent<PhysicsBodyState3D> agent)
    {
        World.AddBody(Description(agent.Placement));
        return new PhysicsBodyLease3D(World, [agent.Id]);
    }

    public override Matrix4x4 WorldTransform(PhysicsBodyState3D state, ScenePlacement placement)
    {
        return state.Pose.Matrix;
    }

    public override SceneGroup Present(PhysicsBodyState3D state) => Visual;

    private PhysicsBody3D Description(ScenePlacement placement)
    {
        Body.Validate();
        Matrix4x4 matrix = Body.Pose.Matrix * placement.WorldTransform;
        Vector3 x = new(matrix.M11, matrix.M12, matrix.M13);
        Vector3 y = new(matrix.M21, matrix.M22, matrix.M23);
        Vector3 z = new(matrix.M31, matrix.M32, matrix.M33);
        if (MathF.Abs(x.LengthSquared() - 1) > .0001f || MathF.Abs(y.LengthSquared() - 1) > .0001f
            || MathF.Abs(z.LengthSquared() - 1) > .0001f || MathF.Abs(Vector3.Dot(x, y)) > .0001f
            || MathF.Abs(Vector3.Dot(x, z)) > .0001f || MathF.Abs(Vector3.Dot(y, z)) > .0001f)
        {
            throw new NotSupportedException("AUR-PHYSICS-002: Physics agents require rigid placement; author dimensions in the shape and visual.");
        }
        var orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
        return Body with
        {
            Id = placement.Id,
            SemanticOwnerId = placement.Id,
            Pose = new(matrix.Translation, orientation),
        };
    }
}

public static class PhysicsScene3D
{
    /// <summary>Adapt existing authored collision geometry. Never infer physics from display-only meshes.</summary>
    public static IDisposable AddStatics(IPhysicsWorld3D world, SpatialWorld3D spatial)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(spatial);
        var descriptions = new List<PhysicsBody3D>();
        HashSet<string> existing = world.CaptureSnapshot().Bodies.Select(body => body.Id).ToHashSet(StringComparer.Ordinal);
        foreach (Collider3D collider in spatial.Colliders)
        {
            if (existing.Contains(collider.Id))
            {
                throw new ArgumentException($"Physics body '{collider.Id}' already exists.");
            }
            var geometry = new CollisionMesh3D(
                collider.Mesh.Positions.Select(point => Vector3.Transform(point, collider.Transform)),
                collider.Mesh.Indices, collider.Mesh.Closed, collider.Mesh.SourceIdentity, collider.Mesh.TriangleFaces);
            var description = new PhysicsBody3D(collider.Id, new PhysicsShape3D.StaticMesh(geometry), PhysicsPose3D.At(Vector3.Zero))
            {
                MotionType = PhysicsMotionType3D.Static,
                Layer = collider.Layer,
                Mask = collider.Mask,
                SemanticOwnerId = collider.SemanticOwnerId,
            };
            description.Validate();
            descriptions.Add(description);
        }
        var added = new List<string>();
        try
        {
            foreach (PhysicsBody3D description in descriptions)
            {
                world.AddBody(description);
                added.Add(description.Id);
            }
        }
        catch
        {
            foreach (string id in added)
            {
                world.RemoveBody(id);
            }
            throw;
        }
        return new PhysicsBodyLease3D(world, added);
    }

    public static void Publish(SceneInstance scene, IPhysicsWorld3D world)
    {
        foreach (SceneAgent agent in scene.Agents)
        {
            if (agent is SceneAgent<PhysicsBodyState3D> bodyAgent
                && bodyAgent.Definition is PhysicsAgentDefinition3D definition
                && ReferenceEquals(definition.World, world))
            {
                bodyAgent.State = world.GetBody(agent.Id);
            }
        }
    }
}

internal sealed class PhysicsBodyLease3D(IPhysicsWorld3D world, IEnumerable<string> ids) : IDisposable
{
    private readonly string[] identities = ids.ToArray();
    private bool disposed;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        foreach (string id in identities)
        {
            world.RemoveBody(id);
        }
    }
}
