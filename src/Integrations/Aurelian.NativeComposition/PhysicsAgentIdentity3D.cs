using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Aurelian.Physics3D;
using Aurelian.World.Scenes;

namespace Aurelian.NativeComposition;

internal static class PhysicsAgentIdentity3D
{
    public static string Compute(PhysicsAgentDefinition3D definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionId);
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true);
        writer.Write("aurelian.physics-agent.v2");
        writer.Write(definition.DefinitionId);
        writer.Write(definition.Template.Id);
        writer.Write((int)definition.Template.Kind);
        writer.Write((int)definition.Template.Control);
        writer.Write(SceneCompiler.Compile(definition.Visual).ContentIdentity);
        PhysicsBody3D body = definition.Body;
        body.Validate();
        writer.Write((int)body.MotionType);
        writer.Write(body.Mass);
        writer.Write(body.Friction);
        writer.Write(body.Layer);
        writer.Write(body.Mask);
        writer.Write((int)body.Continuity);
        writer.Write(body.MaximumSpeculativeMargin);
        writer.Write(body.MinimumSweepSeconds);
        writer.Write(body.SweepConvergenceSeconds);
        WriteVector(writer, body.Pose.Position);
        writer.Write(body.Pose.Orientation.X);
        writer.Write(body.Pose.Orientation.Y);
        writer.Write(body.Pose.Orientation.Z);
        writer.Write(body.Pose.Orientation.W);
        WriteVector(writer, body.Velocity.Linear);
        WriteVector(writer, body.Velocity.Angular);
        WriteShape(writer, body.Shape);
        writer.Write(definition.World.Backend);
        PhysicsWorldOptions3D options = definition.World.Options;
        WriteVector(writer, options.Gravity);
        writer.Write(options.FixedDeltaSeconds);
        writer.Write(options.WorkerCount);
        writer.Write(options.SolverIterations);
        writer.Write(options.Substeps);
        writer.Write(options.CollectContacts);
        writer.Write(options.EnableSleeping);
        writer.Write(options.ContactSpring.Frequency);
        writer.Write(options.ContactSpring.DampingRatio);
        writer.Write(options.MaximumRecoveryVelocity);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
    }

    private static void WriteShape(BinaryWriter writer, PhysicsShape3D shape)
    {
        switch (shape)
        {
            case PhysicsShape3D.Box box:
                writer.Write("box");
                WriteVector(writer, box.Size);
                break;
            case PhysicsShape3D.Sphere sphere:
                writer.Write("sphere");
                writer.Write(sphere.Radius);
                break;
            case PhysicsShape3D.Capsule capsule:
                writer.Write("capsule");
                writer.Write(capsule.Radius);
                writer.Write(capsule.Length);
                break;
            case PhysicsShape3D.StaticMesh mesh:
                writer.Write("static-mesh");
                writer.Write(mesh.Geometry.Positions.Length);
                foreach (Vector3 position in mesh.Geometry.Positions)
                {
                    WriteVector(writer, position);
                }
                writer.Write(mesh.Geometry.Indices.Length);
                foreach (int index in mesh.Geometry.Indices)
                {
                    writer.Write(index);
                }
                break;
            default:
                throw new NotSupportedException("Unsupported physics shape identity.");
        }
    }

    private static void WriteVector(BinaryWriter writer, Vector3 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
        writer.Write(vector.Z);
    }
}
