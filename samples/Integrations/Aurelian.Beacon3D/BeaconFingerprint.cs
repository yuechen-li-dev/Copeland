using System.Numerics;
using System.Security.Cryptography;

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
            writer.Write(shotCooldown);
            writer.Write(reloadRemaining);
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
