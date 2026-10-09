using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Humanoid;

namespace Aurelian.Beacon3D;

public sealed partial class BeaconGame
{
    /// <summary>All game-owned simulation fields, in stable order; independent of presentation and trace retention.</summary>
    public string SemanticHash()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(combat);
            writer.Write((int)View);
            writer.Write(HumanoidOptions?.Identity ?? "geometric");
            writer.Write(verticalVelocity);
            writer.Write(Yaw);
            writer.Write(Pitch);
            writer.Write(Height);
            writer.Write(Time);
            writer.Write(Won);
            writer.Write(Wave);
            writer.Write(WavesCleared);
            writer.Write(Kills);
            writer.Write(Shots);
            writer.Write(Ammo);
            writer.Write(AgentTicks);
            writer.Write(nextWave);
            writer.Write(gun.State.ShotCooldown);
            writer.Write(gun.State.ReloadRemaining);
            writer.Write(hurtRemaining);
            writer.Write(agents.Count);
            foreach (var agent in Agents)
            {
                writer.Write(agent.Id);
                writer.Write(agent.Template.Id);
                WriteVector(writer, agent.State.Position);
                writer.Write(agent.State.Health);
                writer.Write(agent.State.Collected);
                writer.Write(agent.State.Cooldown);
                writer.Write(agent.State.Animation is not null);
                if (agent.State.Animation is { } animation)
                {
                    writer.Write((int)animation.Motion);
                    writer.Write(animation.ClipSeconds);
                    writer.Write(animation.TransitionSeconds);
                    writer.Write(animation.TransitionFrom.Length);
                    foreach (var joint in animation.TransitionFrom)
                    {
                        writer.Write((int)joint.Joint);
                        writer.Write(joint.FlexionDegrees);
                        writer.Write(joint.AbductionDegrees);
                        writer.Write(joint.TwistDegrees);
                    }
                    writer.Write(JsonSerializer.Serialize(animation.Locomotion, HumanoidLocomotionJson.Default.HumanoidLocomotionState));
                }
            }
            writer.Write(bolts.Count);
            foreach (var bolt in bolts)
            {
                WriteVector(writer, bolt.Position);
                WriteVector(writer, bolt.Velocity);
                writer.Write(bolt.Life);
            }
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteVector(BinaryWriter writer, Vector3 point)
    {
        writer.Write(point.X);
        writer.Write(point.Y);
        writer.Write(point.Z);
    }
}

