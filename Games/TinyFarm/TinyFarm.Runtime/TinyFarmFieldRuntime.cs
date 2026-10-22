using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Combat;
using Aurelian.Field2D;

namespace TinyFarm.Core;

public sealed record TinyFarmFieldDefinition(
    string Id,
    SceneId Scene,
    ScenePosition Origin,
    int Width,
    int Height,
    int CellSize,
    string SemanticOwner);

public sealed record TinyFarmFieldSnapshot(
    int Version,
    string DefinitionId,
    ReactiveFluidSnapshot Fluid,
    int ChargedWaterCooldownTicks,
    int PlayerMovementPenaltyTicks,
    long ChargedWaterContactCount);

public sealed record TinyFarmFieldEvent(
    long Tick,
    string Kind,
    string ConceptPath,
    FieldDisturbanceShape? Shape = null,
    double X = 0,
    double Y = 0,
    double Strength = 0,
    int AffectedCells = 0);

public sealed class TinyFarmFieldRuntime
{
    public const int SnapshotVersion = 1;
    public const double ChargedWaterThreshold = 0.35;
    public const int ChargedWaterCooldownTicks = 30;
    public const int ChargedWaterMovementPenaltyTicks = 12;
    public const string ChargeConceptPath = "TinyFarm.World.Pond.Fluid.Charge";
    private const int DiagnosticHistoryCapacity = 16;
    private readonly List<TinyFarmFieldEvent> recentEvents = new(DiagnosticHistoryCapacity);

    private TinyFarmFieldRuntime(TinyFarmFieldDefinition definition, ReactiveFluid2D fluid)
    {
        Definition = definition;
        Fluid = fluid;
    }

    public TinyFarmFieldDefinition Definition { get; }
    public ReactiveFluid2D Fluid { get; }
    public int ChargedWaterCooldownRemaining { get; private set; }
    public int PlayerMovementPenaltyRemaining { get; private set; }
    public long ChargedWaterContactCount { get; private set; }
    public long ProjectionGeneration { get; private set; }
    public IReadOnlyList<TinyFarmFieldEvent> RecentEvents => recentEvents;
    public string SemanticHash => ComputeHash(Capture());

    public static TinyFarmFieldRuntime CreateDefault()
    {
        TinyFarmFieldDefinition definition = DefaultDefinition();
        var fluid = new ReactiveFluid2D(definition.Width, definition.Height, definition.CellSize);
        for (int y = 0; y < definition.Height; y++)
        {
            for (int x = 0; x < definition.Width; x++)
            {
                int index = (y * definition.Width) + x;
                double cell = definition.CellSize / (double)ScenePosition.UnitsPerTile;
                double worldX = definition.Origin.XUnits / (double)ScenePosition.UnitsPerTile + x * cell;
                double worldY = definition.Origin.YUnits / (double)ScenePosition.UnitsPerTile + y * cell;
                bool wet = TinyFarmLandscape.RiverOverlapsCell(worldX, worldY, cell);
                fluid.LiquidMask.Cells[index] = wet ? (byte)1 : (byte)0;
                fluid.ConductiveMask.Cells[index] = wet ? (byte)1 : (byte)0;
                fluid.FlowY.Cells[index] = wet ? 0.025f : 0;
            }
        }
        return new TinyFarmFieldRuntime(definition, fluid);
    }

    public static TinyFarmFieldRuntime Restore(TinyFarmFieldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.DefinitionId == "tinyfarm.riverside.stream.v1")
        {
            return RestoreRectangularStream(snapshot);
        }
        ValidateSnapshot(snapshot);
        TinyFarmFieldRuntime runtime = CreateDefault();
        ReactiveFluidSnapshot authored = runtime.Fluid.Snapshot();
        if (!snapshot.Fluid.LiquidMask.SequenceEqual(authored.LiquidMask)
            || !snapshot.Fluid.ConductiveMask.SequenceEqual(authored.ConductiveMask))
        {
            throw new InvalidDataException("TinyFarm field topology does not match the authored river recipe.");
        }
        runtime.Fluid.RestoreSnapshot(snapshot.Fluid);
        runtime.ChargedWaterCooldownRemaining = snapshot.ChargedWaterCooldownTicks;
        runtime.PlayerMovementPenaltyRemaining = snapshot.PlayerMovementPenaltyTicks;
        runtime.ChargedWaterContactCount = snapshot.ChargedWaterContactCount;
        runtime.ProjectionGeneration = snapshot.Fluid.Tick;
        return runtime;
    }

    public TinyFarmFieldSnapshot Capture()
    {
        return new TinyFarmFieldSnapshot(
            SnapshotVersion,
            Definition.Id,
            Fluid.Snapshot(),
            ChargedWaterCooldownRemaining,
            PlayerMovementPenaltyRemaining,
            ChargedWaterContactCount);
    }

    public bool Contains(SceneId scene, ScenePosition position)
    {
        if (scene != Definition.Scene)
        {
            return false;
        }
        double x = position.XUnits - Definition.Origin.XUnits;
        double y = position.YUnits - Definition.Origin.YUnits;
        return x >= 0
            && y >= 0
            && x < Definition.Width * Definition.CellSize
            && y < Definition.Height * Definition.CellSize
            && TinyFarmLandscape.IsRiver(position.XUnits / (double)ScenePosition.UnitsPerTile,
                position.YUnits / (double)ScenePosition.UnitsPerTile);
    }

    public int ApplyMovement(SceneId scene, ScenePosition from, ScenePosition to)
    {
        if (scene != Definition.Scene || !Contains(scene, to))
        {
            return 0;
        }
        double localX = to.XUnits - Definition.Origin.XUnits;
        double localY = to.YUnits - Definition.Origin.YUnits;
        double deltaX = to.XUnits - from.XUnits;
        double deltaY = to.YUnits - from.YUnits;
        double length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        FieldDisturbance disturbance = length > Definition.CellSize / 2d
            ? new FieldDisturbance(
                FieldDisturbanceShape.Line,
                from.XUnits - Definition.Origin.XUnits,
                from.YUnits - Definition.Origin.YUnits,
                0.16,
                Definition.CellSize,
                Definition.CellSize * 0.8,
                length,
                Math.Atan2(deltaY, deltaX),
                SemanticPayload: "TinyFarm.Player.Movement")
            : new FieldDisturbance(
                FieldDisturbanceShape.Point,
                localX,
                localY,
                0.12,
                Definition.CellSize,
                SemanticPayload: "TinyFarm.Player.Movement");
        return Apply(disturbance, "movement-disturbance");
    }

    public int ApplyCombatMove(
        CombatMoveDefinition move,
        SceneId scene,
        ScenePosition origin,
        ActorFacing facing,
        bool contact)
    {
        ArgumentNullException.ThrowIfNull(move);
        if (scene != Definition.Scene || !Contains(scene, origin))
        {
            return 0;
        }
        double angle = FacingRadians(facing);
        double strength = contact ? 0.72 : 0.28;
        FieldDisturbance disturbance = move.Shape switch
        {
            PointAttackShape point => new FieldDisturbance(
                FieldDisturbanceShape.Point,
                LocalX(origin),
                LocalY(origin),
                strength,
                point.Radius,
                SemanticPayload: move.Id.Value),
            LineAttackShape line => new FieldDisturbance(
                FieldDisturbanceShape.Line,
                LocalX(origin),
                LocalY(origin),
                strength,
                line.Length,
                Math.Max(line.Width, Definition.CellSize * 1.1),
                line.Length,
                angle,
                SemanticPayload: move.Id.Value),
            ArcAttackShape arc => new FieldDisturbance(
                FieldDisturbanceShape.Arc,
                LocalX(origin),
                LocalY(origin),
                strength,
                arc.Radius,
                Definition.CellSize,
                AngleRadians: angle,
                ArcRadians: arc.ArcRadians,
                SemanticPayload: move.Id.Value),
            RingAttackShape ring => new FieldDisturbance(
                FieldDisturbanceShape.Ring,
                LocalX(origin),
                LocalY(origin),
                strength,
                ring.Radius,
                ring.Width,
                SemanticPayload: move.Id.Value),
            _ => throw new InvalidOperationException($"Unsupported combat field shape '{move.Shape.GetType().Name}'.")
        };
        return Apply(disturbance, contact ? "combat-contact" : "combat-motion");
    }

    public int Energize(ScenePosition position, double strength = 1, double radius = ScenePosition.UnitsPerTile * 2)
    {
        if (!Contains(Definition.Scene, position))
        {
            return 0;
        }
        int affected = Fluid.Energize(LocalX(position), LocalY(position), strength, radius);
        if (affected > 0)
        {
            ProjectionGeneration++;
            Remember(new TinyFarmFieldEvent(Fluid.Tick, "charge-applied", ChargeConceptPath,
                X: position.XUnits, Y: position.YUnits, Strength: strength, AffectedCells: affected));
        }
        return affected;
    }

    public bool AdvanceTick(SceneId playerScene, ScenePosition playerPosition)
    {
        Fluid.AdvanceFrame(1.0 / 60.0);
        ProjectionGeneration++;
        if (ChargedWaterCooldownRemaining > 0)
        {
            ChargedWaterCooldownRemaining--;
        }
        if (PlayerMovementPenaltyRemaining > 0)
        {
            PlayerMovementPenaltyRemaining--;
        }
        if (!Contains(playerScene, playerPosition))
        {
            return false;
        }
        float charge = Fluid.Sample(LocalX(playerPosition), LocalY(playerPosition)).Charge;
        if (charge <= ChargedWaterThreshold || ChargedWaterCooldownRemaining > 0)
        {
            return false;
        }
        ChargedWaterCooldownRemaining = ChargedWaterCooldownTicks;
        PlayerMovementPenaltyRemaining = ChargedWaterMovementPenaltyTicks;
        ChargedWaterContactCount++;
        Remember(new TinyFarmFieldEvent(Fluid.Tick, "charged-water-contact", ChargeConceptPath,
            X: playerPosition.XUnits, Y: playerPosition.YUnits, Strength: charge, AffectedCells: 1));
        return true;
    }

    public byte[] ProjectRgba8()
    {
        return Fluid.ProjectRgba8();
    }

    public static string ComputeHash(TinyFarmFieldSnapshot snapshot)
    {
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(snapshot, TinyFarmFieldJsonContext.Default.TinyFarmFieldSnapshot);
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    private int Apply(FieldDisturbance disturbance, string kind)
    {
        int affected = Fluid.ApplyDisturbance(disturbance);
        if (affected > 0)
        {
            ProjectionGeneration++;
            Remember(new TinyFarmFieldEvent(
                Fluid.Tick,
                kind,
                "TinyFarm.World.Pond.Fluid.Disturbance",
                disturbance.Shape,
                disturbance.X + Definition.Origin.XUnits,
                disturbance.Y + Definition.Origin.YUnits,
                disturbance.Strength,
                affected));
        }
        return affected;
    }

    private void Remember(TinyFarmFieldEvent item)
    {
        if (recentEvents.Count == DiagnosticHistoryCapacity)
        {
            recentEvents.RemoveAt(0);
        }
        recentEvents.Add(item);
    }

    private double LocalX(ScenePosition position) => position.XUnits - Definition.Origin.XUnits;
    private double LocalY(ScenePosition position) => position.YUnits - Definition.Origin.YUnits;

    private static double FacingRadians(ActorFacing facing)
    {
        return facing switch
        {
            ActorFacing.Right => 0,
            ActorFacing.Down => Math.PI / 2,
            ActorFacing.Left => Math.PI,
            ActorFacing.Up => -Math.PI / 2,
            _ => throw new ArgumentOutOfRangeException(nameof(facing))
        };
    }

    private static TinyFarmFieldDefinition DefaultDefinition()
    {
        return new TinyFarmFieldDefinition(
            "tinyfarm.riverside.stream.v2",
            TinyFarmSceneIds.Riverside,
            new ScenePosition(
                7 * ScenePosition.UnitsPerTile,
                -2 * ScenePosition.UnitsPerTile),
            Width: 20,
            Height: 28,
            CellSize: ScenePosition.UnitsPerTile / 2,
            SemanticOwner: "TinyFarmSession");
    }

    private static TinyFarmFieldRuntime RestoreRectangularStream(TinyFarmFieldSnapshot snapshot)
    {
        // Explicit migration of the previous authored domain. Preserve dynamics in the spatial overlap only.
        var old = new ReactiveFluid2D(14, 22, ScenePosition.UnitsPerTile / 2);
        if (snapshot.Version != SnapshotVersion || snapshot.ChargedWaterCooldownTicks < 0
            || snapshot.PlayerMovementPenaltyTicks < 0 || snapshot.ChargedWaterContactCount < 0)
        {
            throw new InvalidDataException("Invalid legacy river snapshot.");
        }
        for (int y = 0; y < 22; y++)
        {
            for (int x = 0; x < 14; x++)
            {
                byte expected = x > 0 && x < 13 && y > 0 && y < 21 ? (byte)1 : (byte)0;
                int index = y * 14 + x;
                if (snapshot.Fluid.LiquidMask.Length != 308 || snapshot.Fluid.ConductiveMask.Length != 308
                    || snapshot.Fluid.LiquidMask[index] != expected || snapshot.Fluid.ConductiveMask[index] != expected)
                {
                    throw new InvalidDataException("Legacy river topology is not the authored rectangle.");
                }
            }
        }
        old.RestoreSnapshot(snapshot.Fluid);
        var current = CreateDefault();
        ReactiveFluidSnapshot target = current.Fluid.Snapshot();
        for (int y = 0; y < target.Height; y++)
        {
            for (int x = 0; x < target.Width; x++)
            {
                int index = y * target.Width + x;
                int oldX = x - 5;
                int oldY = y - 3;
                if (target.LiquidMask[index] == 0 || oldX < 0 || oldX >= 14 || oldY < 0 || oldY >= 22
                    || old.LiquidMask[oldX, oldY] == 0)
                {
                    continue;
                }
                int source = oldY * 14 + oldX;
                target.HeightField[index] = snapshot.Fluid.HeightField[source];
                target.Velocity[index] = snapshot.Fluid.Velocity[source];
                target.Foam[index] = snapshot.Fluid.Foam[source];
                target.Charge[index] = snapshot.Fluid.Charge[source];
                target.FlowX[index] = snapshot.Fluid.FlowX[source];
                target.FlowY[index] = snapshot.Fluid.FlowY[source];
            }
        }
        return Restore(snapshot with { DefinitionId = current.Definition.Id, Fluid = target with { Tick = snapshot.Fluid.Tick } });
    }

    private static void ValidateSnapshot(TinyFarmFieldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        TinyFarmFieldDefinition definition = DefaultDefinition();
        if (snapshot.Version != SnapshotVersion
            || !string.Equals(snapshot.DefinitionId, definition.Id, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unsupported TinyFarm field snapshot version or definition.");
        }
        if (snapshot.ChargedWaterCooldownTicks < 0
            || snapshot.PlayerMovementPenaltyTicks < 0
            || snapshot.ChargedWaterContactCount < 0)
        {
            throw new InvalidDataException("TinyFarm charged-water state cannot be negative.");
        }
    }
}

// Preserve the original PascalCase/numeric-enum hash bytes while removing reflection serialization.
[System.Text.Json.Serialization.JsonSerializable(typeof(TinyFarmFieldSnapshot))]
internal partial class TinyFarmFieldJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
