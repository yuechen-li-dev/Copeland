using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Aurelian.StrategyDemo;

/// <summary>Dominatus owns resumable phases; proposals are applied by the session in stable unit order.</summary>
internal sealed class WorkerBehaviors(StrategySession session)
{
    private readonly List<(StrategyUnit Unit, AiWorld World)> agents = [];
    private readonly SortedDictionary<int, Action> proposals = [];

    public void Add(StrategyUnit unit)
    {
        var graph = new HfsmGraph { Root = "route" };
        graph.Add("route", _ => Route(unit));
        graph.Add("harvest", _ => Harvest(unit));
        graph.Add("return", _ => Return(unit));
        var world = new AiWorld();
        world.Add(new AiAgent(new HfsmInstance(graph)));
        agents.Add((unit, world));
    }

    public void Tick()
    {
        proposals.Clear();
        foreach ((StrategyUnit _, AiWorld world) in agents)
        {
            world.Tick(.1f);
        }
        foreach (Action proposal in proposals.Values)
        {
            proposal();
        }
    }

    private IEnumerator<AiStep> Route(StrategyUnit unit)
    {
        while (true)
        {
            long tick = session.Tick;
            switch (unit.Order)
            {
                case GatherOrder gather:
                    ResourceNode node = session.Resources.Single(r => r.Id == gather.ResourceId);
                    if (unit.Carry >= 10 || unit.Carry > 0 && (node.Amount == 0 || unit.CarryKind != node.Kind))
                    {
                        yield return Ai.Goto("return");
                        yield break;
                    }
                    if (node.Amount == 0)
                    {
                        Stage(unit, "idle", () => unit.Order = new IdleOrder());
                    }
                    else if (Near(unit, node.Position, 1.0))
                    {
                        yield return Ai.Goto("harvest");
                        yield break;
                    }
                    else
                    {
                        Stage(unit, "to resource", () => session.MoveToward(unit, node.Position, .9));
                    }
                    break;
                case MoveOrder move:
                    Stage(unit, "moving", () =>
                    {
                        session.MoveToward(unit, move.Target, .05);
                        if (Near(unit, move.Target, .06))
                        {
                            unit.Order = new IdleOrder();
                        }
                    });
                    break;
                case ConstructOrder construct:
                    StrategyBuilding building = session.Buildings.Single(b => b.Id == construct.BuildingId);
                    if (Near(unit, building.Position, 1.8))
                    {
                        Stage(unit, "constructing", () => session.Construct(unit, building));
                    }
                    else
                    {
                        Stage(unit, "to site", () => session.MoveToward(unit, building.Position, 1.7));
                    }
                    break;
                case AttackOrder:
                    if (session.EnemyHealth == 0 || !session.IsVisible(StrategySession.EnemyPosition))
                    {
                        Stage(unit, "idle", () => unit.Order = new IdleOrder());
                    }
                    else if (Near(unit, StrategySession.EnemyPosition, AttackRange(unit)))
                    {
                        Stage(unit, "attacking", () => session.Attack(unit));
                    }
                    else
                    {
                        Stage(unit, "to target", () => session.MoveToward(unit, StrategySession.EnemyPosition, AttackRange(unit) - .1));
                    }
                    break;
                default:
                    Stage(unit, "idle", () => { });
                    break;
            }
            yield return Ai.Until(_ => session.Tick > tick);
        }
    }

    private IEnumerator<AiStep> Harvest(StrategyUnit unit)
    {
        while (unit.Order is GatherOrder gather)
        {
            ResourceNode node = session.Resources.Single(r => r.Id == gather.ResourceId);
            if (unit.Carry >= 10 || node.Amount == 0 || !Near(unit, node.Position, 1.1)
                || unit.Carry > 0 && unit.CarryKind != node.Kind)
            {
                break;
            }
            long tick = session.Tick;
            Stage(unit, "harvesting", () => session.Harvest(unit, node));
            yield return Ai.Until(_ => session.Tick > tick);
        }
        yield return Ai.Goto("route");
    }

    private IEnumerator<AiStep> Return(StrategyUnit unit)
    {
        while (unit.Order is GatherOrder && unit.Carry > 0)
        {
            long tick = session.Tick;
            if (Near(unit, StrategySession.Home, 1.8))
            {
                Stage(unit, "dropoff", () => session.Deposit(unit));
            }
            else
            {
                Stage(unit, "carrying", () => session.MoveToward(unit, StrategySession.Home, 1.7));
            }
            yield return Ai.Until(_ => session.Tick > tick);
        }
        yield return Ai.Goto("route");
    }

    private void Stage(StrategyUnit unit, string phase, Action resolve)
    {
        proposals.Add(unit.Id, () =>
        {
            unit.Phase = phase;
            resolve();
        });
    }

    public static double AttackRange(StrategyUnit unit) => unit.Kind switch
    {
        UnitKind.Ranger => 4.2,
        UnitKind.Soldier => 1.4,
        _ => 1.0,
    };

    private static bool Near(StrategyUnit unit, Aurelian.Spatial2D.SpatialPoint2D point, double range)
        => (unit.Position - point).LengthSquared <= range * range;
}
