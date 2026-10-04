using Aurelian.Spatial2D;

namespace TinyFarm.Core;

public sealed partial class TinyFarmResolver
{
    internal IntentResult ReduceSliceTick(TinyFarmState state, IntentEnvelope envelope)
    {
        return ResolveSliceTick(state, envelope);
    }

    private IntentResult ResolveSliceSword(TinyFarmState state, IntentEnvelope envelope)
    {
        if (state.Slice is not TinyFarmSliceState slice || slice.DodgeTicks > 0
            || envelope.Actor != TinyFarmIds.Player || !OwnsItem(state, state.Actor(envelope.Actor), TinyFarmIds.Sword))
        {
            return NoOp(envelope, IntentReason.WrongWeapon);
        }
        if (slice.SwordTicks > 0)
        {
            state.Slice = slice with { SwordBuffered = true };
            return Accepted(envelope);
        }
        state.Slice = slice with { SwordTicks = 18, SwordHit = false, SwordBuffered = false };
        return Accepted(envelope, new GameEvent(GameEventKind.SwordStarted, envelope.Actor));
    }

    private IntentResult ResolveSliceDodge(TinyFarmState state, IntentEnvelope envelope, DodgeIntent intent)
    {
        if (state.Slice is not TinyFarmSliceState slice || slice.DodgeCooldown > 0 || envelope.Actor != TinyFarmIds.Player)
        {
            return NoOp(envelope, IntentReason.InvalidMovement);
        }
        int x = Math.Clamp(intent.X, -1, 1);
        int y = Math.Clamp(intent.Y, -1, 1);
        if (x == 0 && y == 0)
        {
            (x, y) = FacingVector(state.ActorScene(TinyFarmIds.Player).Facing);
        }
        state.Slice = slice with
        {
            DodgeTicks = 15,
            DodgeCooldown = 36,
            SwordTicks = 0,
            SwordBuffered = false,
            DodgeDirection = new ScenePosition(x, y)
        };
        return Accepted(envelope, new GameEvent(GameEventKind.PlayerDodged, envelope.Actor));
    }

    private IntentResult ResolveSliceEat(TinyFarmState state, IntentEnvelope envelope)
    {
        var broth = new ProductId("turnip-broth");
        if (state.Slice is not TinyFarmSliceState slice || envelope.Actor != TinyFarmIds.Player || state.ProductCount(envelope.Actor, broth) < 1)
        {
            return Rejected(envelope, IntentReason.MissingIngredient);
        }
        if (slice.Health == 12)
        {
            return NoOp(envelope, IntentReason.None);
        }
        SetProductCount(state, envelope.Actor, broth, state.ProductCount(envelope.Actor, broth) - 1);
        state.Slice = slice with { Health = Math.Min(12, slice.Health + 4) };
        return Accepted(envelope, new GameEvent(GameEventKind.PlayerHealed, envelope.Actor, Amount: Math.Min(4, 12 - slice.Health)));
    }

    private IntentResult ResolveSliceSleep(TinyFarmState state, IntentEnvelope envelope)
    {
        if (state.Slice is not TinyFarmSliceState slice || envelope.Actor != TinyFarmIds.Player || state.CurrentScene != TinyFarmSceneIds.Residence
            || TinyFarmSpatialQueries.SelectObjectTarget(state, TinyFarmIds.Player,
                new SceneObjectId("player-bed"), Scenes) is null)
        {
            return Rejected(envelope, IntentReason.WrongLocation);
        }
        int minutes = 1440 - state.Minute % 1440 + 360;
        var events = new List<GameEvent>();
        int tomorrow = state.Day + 1;
        state.Minute += minutes;
        AdvanceDay(state, envelope.Actor, tomorrow, events);
        state.Slice = slice with { Health = 12, HurtTicks = 0, SwordTicks = 0, SwordBuffered = false, DodgeTicks = 0, Slept = true };
        return new IntentResult(envelope, IntentResultStatus.Accepted, IntentReason.None, events);
    }

    private void ReturnSlicePlayerForRest(TinyFarmState state, List<GameEvent> events)
    {
        ActorSceneState player = state.ActorScene(TinyFarmIds.Player);
        SceneDefinition house = Scenes.Get(TinyFarmSceneIds.Residence);
        SceneLayoutRow bed = house.Layout.Single(row => row.ObjectId.Value == "player-bed");
        ReplaceActorScene(state, player with
        {
            Scene = house.Id,
            WorldPosition = new ScenePosition((bed.X + bed.Width / 2) * 1024, (bed.Y + bed.Height) * 1024 + 512),
            Facing = ActorFacing.Up
        });
        ReplaceActor(state, state.Actor(TinyFarmIds.Player) with { Location = TinyFarmIds.Farmhouse });
        int tomorrow = state.Day + 1;
        state.Minute = (tomorrow - 1) * 1440 + 360;
        AdvanceDay(state, TinyFarmIds.Player, tomorrow, events);
        state.Slice = state.Slice! with
        {
            Health = 12,
            HurtTicks = 0,
            SwordTicks = 0,
            SwordBuffered = false,
            DodgeTicks = 0,
            Slept = true,
            ReturnedHome = state.Slice.Defeats > 0 || state.Slice.ReturnedHome
        };
        events.Add(new GameEvent(GameEventKind.PlayerReturnedForRest, TinyFarmIds.Player));
    }

    private IntentResult ResolveSliceTick(TinyFarmState state, IntentEnvelope envelope)
    {
        if (state.Slice is not TinyFarmSliceState current || envelope.Actor != TinyFarmIds.Player)
        {
            return NoOp(envelope, IntentReason.None);
        }
        var events = new List<GameEvent>();
        var slice = current with
        {
            Tick = current.Tick + 1,
            HurtTicks = Math.Max(0, current.HurtTicks - 1),
            SwordTicks = Math.Max(0, current.SwordTicks - 1),
            DodgeTicks = Math.Max(0, current.DodgeTicks - 1),
            DodgeCooldown = Math.Max(0, current.DodgeCooldown - 1)
        };
        if (current.SwordTicks == 1 && current.SwordBuffered)
        {
            slice = slice with { SwordTicks = 18, SwordHit = false, SwordBuffered = false };
            events.Add(new GameEvent(GameEventKind.SwordStarted, envelope.Actor));
        }
        ActorSceneState player = state.ActorScene(TinyFarmIds.Player);
        if (current.DodgeTicks > 0)
        {
            ResolveSpatialMoveCore(state, TinyFarmIds.Player, current.DodgeDirection.XUnits,
                current.DodgeDirection.YUnits, 105);
            player = state.ActorScene(TinyFarmIds.Player);
        }

        EnemyDefinition enemy = definitions!.Enemy(TinyFarmIds.DungeonSlime);
        int enemyIndex = state.MutableEnemies.FindIndex(item => item.Id == enemy.Id);
        EnemyState enemyState = state.MutableEnemies[enemyIndex];
        if (player.Scene == enemy.Scene && enemyState.CurrentHealth > 0)
        {
            double dx = player.WorldPosition.XUnits - slice.SlimePosition.XUnits;
            double dy = player.WorldPosition.YUnits - slice.SlimePosition.YUnits;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (slice.SwordTicks is >= 10 and <= 14 && !slice.SwordHit && distance < 1450)
            {
                (int facingX, int facingY) = FacingVector(player.Facing);
                if (dx * facingX + dy * facingY < -distance * .25)
                {
                    int health = Math.Max(0, enemyState.CurrentHealth - 2);
                    state.MutableEnemies[enemyIndex] = enemyState with { CurrentHealth = health };
                    slice = slice with { SwordHit = true, SlimePhase = SlimePhase.Recover, SlimeTicks = 36 };
                    events.Add(new GameEvent(health == 0 ? GameEventKind.EnemyDefeated : GameEventKind.SwordConnected,
                        envelope.Actor, Enemy: enemy.Id, EnemyKind: enemy.Kind, Amount: 2, Scene: enemy.Scene));
                    if (health == 0)
                    {
                        slice = slice with { Defeats = slice.Defeats + 1 };
                    }
                }
            }

            int phaseTicks = slice.SlimeTicks - 1;
            if (phaseTicks <= 0)
            {
                SlimePhase phase = slice.SlimePhase;
                if (phase is SlimePhase.Rest or SlimePhase.Recover)
                {
                    slice = slice with { SlimePhase = SlimePhase.Watch, SlimeTicks = 35 };
                }
                else if (phase == SlimePhase.Watch)
                {
                    slice = slice with
                    {
                        SlimePhase = SlimePhase.Windup,
                        SlimeTicks = 42,
                        SlimeDirection = new ScenePosition((int)dx, (int)dy)
                    };
                }
                else if (phase == SlimePhase.Windup)
                {
                    slice = slice with { SlimePhase = SlimePhase.Lunge, SlimeTicks = 18 };
                }
                else
                {
                    slice = slice with { SlimePhase = SlimePhase.Recover, SlimeTicks = 54 };
                }
            }
            else
            {
                slice = slice with { SlimeTicks = phaseTicks };
            }
            if (slice.SlimePhase == SlimePhase.Lunge && state.MutableEnemies[enemyIndex].CurrentHealth > 0)
            {
                ScenePosition direction = slice.SlimeDirection;
                double length = Math.Max(1, Math.Sqrt((double)direction.XUnits * direction.XUnits + (double)direction.YUnits * direction.YUnits));
                int stepX = (int)Math.Round(direction.XUnits / length * 95);
                int stepY = (int)Math.Round(direction.YUnits / length * 95);
                ScenePosition position = slice.SlimePosition;
                ScenePosition target = new(position.XUnits + stepX, position.YUnits + stepY);
                if (TinyFarmScenes.IsInBounds(Scenes.Get(enemy.Scene), target)
                    && spatialWorlds[enemy.Scene].Sweep(TinyFarmSpatialWorldAdapter.ActorPoint(position), new SpatialVector2D(stepX, stepY)) is null)
                {
                    slice = slice with { SlimePosition = target };
                }
                dx = player.WorldPosition.XUnits - slice.SlimePosition.XUnits;
                dy = player.WorldPosition.YUnits - slice.SlimePosition.YUnits;
                if (dx * dx + dy * dy < 650 * 650 && slice.HurtTicks == 0 && current.DodgeTicks == 0)
                {
                    slice = slice with { Health = Math.Max(0, slice.Health - 2), HurtTicks = 42 };
                    events.Add(new GameEvent(GameEventKind.PlayerHurt, envelope.Actor, Amount: 2));
                }
            }
        }
        if (slice.Health == 0)
        {
            SceneAnchorDefinition entrance = Scenes.Get(enemy.Scene).Anchors.First(anchor => anchor.Kind == SceneAnchorKind.Spawn);
            ReplaceActorScene(state, player with { WorldPosition = entrance.Position });
            state.MutableEnemies[enemyIndex] = enemyState with { CurrentHealth = enemy.MaxHealth };
            slice = slice with
            {
                Health = 6,
                HurtTicks = 90,
                SwordTicks = 0,
                SwordBuffered = false,
                DodgeTicks = 0,
                SlimePosition = enemy.SpawnPosition,
                SlimePhase = SlimePhase.Rest,
                SlimeTicks = 90
            };
            events.Add(new GameEvent(GameEventKind.PlayerRescued, envelope.Actor));
        }
        state.Slice = slice;
        return new IntentResult(envelope, IntentResultStatus.Accepted, IntentReason.None, events);
    }

    private static (int X, int Y) FacingVector(ActorFacing facing) => facing switch
    {
        ActorFacing.Left => (-1, 0),
        ActorFacing.Right => (1, 0),
        ActorFacing.Up => (0, -1),
        _ => (0, 1)
    };
}
