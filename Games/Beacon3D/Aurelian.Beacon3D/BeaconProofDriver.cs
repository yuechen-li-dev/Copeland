using System.Numerics;
using InputMan.Core;

namespace Aurelian.Beacon3D;

/// <summary>Proof controls inject physical InputMan state; they never change game facts.</summary>
public static class BeaconProofDriver
{
    public static void Fight(BeaconGame game, BeaconControls controls)
    {
        var target = game.Creatures
            .Where(agent => agent.State.Health > 0)
            .OrderBy(agent => Vector3.DistanceSquared(game.Eye, agent.State.Position))
            .FirstOrDefault(agent => Visible(game, agent.State.Position + Vector3.UnitY));
        if (target is not null)
        {
            Aim(game, controls, target.State.Position + Vector3.UnitY);
        }
        controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), target is not null);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.R), game.Ammo == 0 && !game.Reloading);
    }

    public static void Aim(BeaconGame game, BeaconControls controls, Vector3 target)
    {
        Vector3 direction = Vector3.Normalize(target - game.Eye);
        float yaw = MathF.Atan2(direction.X, -direction.Z);
        float pitch = MathF.Asin(direction.Y);
        controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX),
            MathF.IEEERemainder(yaw - game.Yaw, MathF.Tau) / BeaconControls.MouseSensitivity);
        controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaY),
            -(pitch - game.Pitch) / BeaconControls.MouseSensitivity);
    }

    private static bool Visible(BeaconGame game, Vector3 target)
    {
        Vector3 delta = target - game.Eye;
        float distance = delta.Length();
        return BeaconGame.WorldHit(game.Eye, delta / distance, distance) >= distance;
    }
}
