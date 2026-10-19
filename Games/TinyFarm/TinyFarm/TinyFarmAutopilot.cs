using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm;

/// <summary>
/// Right-click-to-go. A click on the world becomes a path (DotRecast over the scene) that the player walks through the
/// ordinary movement input, one tick at a time, so collision, cadence and replay are unchanged. A click on something
/// you can use (a person, a door, a plot, a forage node, the stove, a ground item) walks next to it, faces it and
/// interacts. Any movement input, a menu, a conversation or a scene change cancels the walk.
/// </summary>
internal sealed class TinyFarmAutopilot(TinyFarmGame game)
{
    // Close enough to a waypoint to aim at the next one (an eighth of a tile).
    private const int Arrival = ScenePosition.UnitsPerTile / 8;
    private const int MaximumTicks = 60 * 30;
    private readonly DotRecastNavigationPlanner navigation = new();
    private readonly Queue<ScenePosition> waypoints = new();
    private SceneId scene;
    private TinyFarmAutopilotTarget? target;
    private int ticks;

    public bool Active => waypoints.Count > 0 || target is not null;

    /// <summary>Walk to a point in the current scene, or to whatever is there that can be used.</summary>
    public bool Go(ScenePosition point)
    {
        Cancel();
        ActorSceneState player = game.State.ActorScene(TinyFarmIds.Player);
        SceneDefinition definition = game.Definitions.Scenes.Get(player.Scene);
        TinyFarmAutopilotTarget? found = FindTarget(definition, point);
        IEnumerable<ScenePosition> goals = found is { } usable ? Approaches(definition, usable.Center) : [point];
        foreach (ScenePosition goal in goals)
        {
            NavigationPath path = navigation.FindPath(definition, player.WorldPosition, goal);
            if (!path.Succeeded)
            {
                continue;
            }
            foreach (ScenePosition waypoint in path.Waypoints.Skip(1))
            {
                waypoints.Enqueue(waypoint);
            }
            scene = player.Scene;
            target = found;
            ticks = 0;
            return true;
        }
        return false;
    }

    public void Cancel()
    {
        waypoints.Clear();
        target = null;
    }

    /// <summary>
    /// The movement to apply this tick (-1, 0 or 1 per axis, +Y up like the move axis), or null when idle.
    /// Interaction happens here too, once the player stands beside the target.
    /// </summary>
    public (int X, int Y)? Steer(bool playerMoving)
    {
        if (!Active)
        {
            return null;
        }
        ActorSceneState player = game.State.ActorScene(TinyFarmIds.Player);
        if (playerMoving || game.CapturesGameplay || player.Scene != scene || ticks++ > MaximumTicks)
        {
            Cancel();
            return null;
        }
        while (waypoints.TryPeek(out ScenePosition next) && Near(player.WorldPosition, next))
        {
            waypoints.Dequeue();
        }
        if (waypoints.TryPeek(out ScenePosition aim))
        {
            int dx = aim.XUnits - player.WorldPosition.XUnits;
            int dy = aim.YUnits - player.WorldPosition.YUnits;
            int x = Math.Abs(dx) > Arrival / 2 ? Math.Sign(dx) : 0;
            int y = Math.Abs(dy) > Arrival / 2 ? Math.Sign(dy) : 0;
            return (x, -y);
        }
        if (target is { } arrived)
        {
            target = null;
            int faceX = Math.Sign(arrived.Center.XUnits - player.WorldPosition.XUnits);
            int faceY = Math.Sign(arrived.Center.YUnits - player.WorldPosition.YUnits);
            if (Math.Abs(arrived.Center.XUnits - player.WorldPosition.XUnits) >= Math.Abs(arrived.Center.YUnits - player.WorldPosition.YUnits))
            {
                faceY = 0;
            }
            else
            {
                faceX = 0;
            }
            game.Execute(new SpatialMoveIntent(faceX, faceY, 1));
            game.Execute(new InteractIntent());
        }
        return null;
    }

    private static bool Near(ScenePosition a, ScenePosition b) =>
        Math.Abs(a.XUnits - b.XUnits) <= Arrival && Math.Abs(a.YUnits - b.YUnits) <= Arrival;

    /// <summary>The closest usable thing within half a tile of the click: actors first, then objects and items.</summary>
    private TinyFarmAutopilotTarget? FindTarget(SceneDefinition definition, ScenePosition point)
    {
        const int Reach = ScenePosition.UnitsPerTile * 3 / 4;
        TinyFarmAutopilotTarget? best = null;
        long bestDistance = long.MaxValue;
        void Consider(ScenePosition center)
        {
            long dx = center.XUnits - point.XUnits;
            long dy = center.YUnits - point.YUnits;
            long distance = dx * dx + dy * dy;
            if (Math.Abs(dx) <= Reach && Math.Abs(dy) <= Reach && distance < bestDistance)
            {
                bestDistance = distance;
                best = new TinyFarmAutopilotTarget(center);
            }
        }
        foreach (ActorSceneState actor in game.State.ActorScenes)
        {
            if (actor.Actor != TinyFarmIds.Player && actor.Scene == definition.Id)
            {
                Consider(actor.WorldPosition);
            }
        }
        foreach (SceneLayoutRow row in definition.Layout)
        {
            SceneObjectKind kind = definition.Object(row.ObjectId).Kind;
            if (kind is SceneObjectKind.Portal or SceneObjectKind.Plot or SceneObjectKind.Forage
                or SceneObjectKind.CookingStation or SceneObjectKind.Bed or SceneObjectKind.Tree)
            {
                Consider(new ScenePosition(
                    row.X * ScenePosition.UnitsPerTile + row.Width * ScenePosition.UnitsPerTile / 2,
                    row.Y * ScenePosition.UnitsPerTile + row.Height * ScenePosition.UnitsPerTile / 2));
            }
        }
        foreach (ItemState item in game.State.Items)
        {
            if (item.GroundScene == definition.Id && item.GroundPosition is { } position)
            {
                Consider(position);
            }
        }
        return best;
    }

    /// <summary>Walkable tiles beside a target, nearest the player first.</summary>
    private IEnumerable<ScenePosition> Approaches(SceneDefinition definition, ScenePosition center)
    {
        ScenePosition player = game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        (int X, int Y)[] sides = [(0, 1), (1, 0), (-1, 0), (0, -1)];
        return sides
            .Select(side => new ScenePosition(center.XUnits + side.X * ScenePosition.UnitsPerTile,
                center.YUnits + side.Y * ScenePosition.UnitsPerTile))
            .Where(position => TinyFarmScenes.IsInBounds(definition, position) && !TinyFarmScenes.IsBlocked(definition, position))
            .OrderBy(position => Math.Abs((long)position.XUnits - player.XUnits) + Math.Abs((long)position.YUnits - player.YUnits))
            .ToArray();
    }
}

internal readonly record struct TinyFarmAutopilotTarget(ScenePosition Center);
