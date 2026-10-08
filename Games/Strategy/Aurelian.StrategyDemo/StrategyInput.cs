using Aurelian.GameHost;
using Aurelian.Spatial2D;
using Aurelian.Strategy;
using InputMan.Aurelian;
using InputMan.Core;

namespace Aurelian.StrategyDemo;

public sealed class StrategyInput : IDisposable
{
    private static readonly ActionMapId Map = new("Strategy");
    private static readonly ActionId Select = new("Select");
    private static readonly ActionId Command = new("Command");
    private static readonly ActionId Additive = new("Additive");
    private static readonly ActionId Store = new("Store");
    private static readonly AxisId Wheel = new("Zoom");
    private static readonly AxisId PanX = new("PanX");
    private static readonly AxisId PanY = new("PanY");
    private static readonly (KeyboardKey Key, string Action)[] Keys =
    [
        (KeyboardKey.B, "Build"), (KeyboardKey.N, "Recruit"), (KeyboardKey.S, "Stop"),
        (KeyboardKey.I, "Idle"), (KeyboardKey.Space, "Center"), (KeyboardKey.Escape, "Cancel"),
        (KeyboardKey.F, "Ranger"),
        (KeyboardKey.Number1, "Group1"), (KeyboardKey.Number2, "Group2"), (KeyboardKey.Number3, "Group3"),
    ];
    private readonly StrategySession session;
    private readonly StrategyView view;
    private readonly AurelianInputAdapter adapter;
    private long lastClickTicks = -TimeSpan.TicksPerSecond;
    private int? lastClickId;
    private bool focused = true;

    public StrategyInput(StrategySession session, StrategyView view)
    {
        this.session = session;
        this.view = view;
        List<Binding> bindings =
        [
            Bind.Action(Controls.Mouse(MouseButton.Primary), Select, ButtonEdge.Down),
            Bind.Action(Controls.Mouse(MouseButton.Secondary), Command),
            Bind.Action(Controls.Key(KeyboardKey.LeftShift), Additive, ButtonEdge.Down),
            Bind.Action(Controls.Key(KeyboardKey.LeftControl), Store, ButtonEdge.Down),
            Bind.Axis(Controls.Mouse(MouseAxis.WheelY), Wheel),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowLeft), PanX, -1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowRight), PanX, 1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowUp), PanY, -1),
            Bind.ButtonAxis(Controls.Key(KeyboardKey.ArrowDown), PanY, 1),
        ];
        bindings.AddRange(Keys.Select(key => Bind.Action(Controls.Key(key.Key), new(key.Action))));
        adapter = new(new InputManEngine(Input.Profile([Input.Map(Map, 0, bindings)])));
        adapter.SetContexts(Map);
    }

    public void RecordButton(ControlKey key, bool down) => adapter.RecordButton(key, down);
    public void RecordWheel(float amount) => adapter.RecordAxis(Controls.Mouse(MouseAxis.WheelY), amount);

    public void Focus(bool value)
    {
        focused = value;
        adapter.OnFocusChanged(value);
        view.DragStart = null;
    }

    public void Update(TimeSpan elapsed, TimeSpan total)
    {
        adapter.BeginFrame(new((ulong)total.Ticks, elapsed, total));
        InputFrame frame = adapter.CurrentFrame;
        if (!focused)
        {
            return;
        }
        float x = view.ScreenPointer.X;
        float y = view.ScreenPointer.Y;
        view.Pointer = view.World(x, y);
        if (view.Placing)
        {
            view.Pointer = new(Math.Round(view.Pointer.X), Math.Round(view.Pointer.Y));
        }
        bool hud = y >= 570 || y < 78 || x < 304 && y is >= 102 and <= 292;
        if (frame.WasPressed(Select) && !hud)
        {
            view.DragStart = (x, y);
        }
        if (frame.IsDown(Select) && StrategyView.OnMinimap(x, y))
        {
            view.MinimapJump(x, y);
            view.DragStart = null;
        }
        if (frame.WasReleased(Select))
        {
            if (y is >= 659 and <= 753 && x is >= 361 and < 965)
            {
                string[] actions = ["Build", "Recruit", "Stop", "Idle"];
                RunAction(actions[Math.Clamp((int)(x - 361) / 151, 0, 3)], frame);
            }
            else if (view.DragStart is { } start && !hud)
            {
                SelectWorld(start, x, y, frame.IsDown(Additive), total);
            }
            view.DragStart = null;
        }
        if (frame.WasPressed(Command) && !hud)
        {
            ContextOrder();
        }
        foreach ((KeyboardKey _, string action) in Keys)
        {
            if (frame.WasPressed(new(action)))
            {
                RunAction(action, frame);
            }
        }
        if (!hud)
        {
            view.Zoom(frame.GetAxis(Wheel) * .1);
        }
        double dx = frame.GetAxis(PanX);
        double dy = frame.GetAxis(PanY);
        if (!hud && view.DragStart is null)
        {
            if (x < 12)
            {
                dx--;
            }
            if (x > 1268)
            {
                dx++;
            }
            if (y < 90)
            {
                dy--;
            }
            if (y > 558)
            {
                dy++;
            }
        }
        view.Pan(dx * elapsed.TotalSeconds * 300, dy * elapsed.TotalSeconds * 300);
    }

    private void SelectWorld((float X, float Y) start, float x, float y, bool additive, TimeSpan total)
    {
        if (view.Placing)
        {
            IntentResult result = session.Dispatch(new BuildIntent(new(Math.Round(view.Pointer.X), Math.Round(view.Pointer.Y))));
            if (result.Accepted)
            {
                view.Placing = false;
            }
            return;
        }
        SelectionChange change = additive ? SelectionChange.Add : SelectionChange.Replace;
        int[] ids;
        if (Math.Abs(start.X - x) + Math.Abs(start.Y - y) > 8)
        {
            // Isometric screen rectangle is a parallelogram in world space: broad query, then exact screen filter.
            SpatialPoint2D[] corners = [view.World(start.X, start.Y), view.World(x, start.Y), view.World(x, y), view.World(start.X, y)];
            double minX = corners.Min(p => p.X);
            double minY = corners.Min(p => p.Y);
            double maxX = corners.Max(p => p.X);
            double maxY = corners.Max(p => p.Y);
            ids = session.Query(new Aabb2(new((minX + maxX) / 2, (minY + maxY) / 2), new((maxX - minX) / 2, (maxY - minY) / 2)))
                .Where(id =>
                {
                    var point = view.Screen(session.Units.Single(u => u.Id == id).Position);
                    return point.X >= Math.Min(start.X, x) && point.X <= Math.Max(start.X, x)
                        && point.Y >= Math.Min(start.Y, y) && point.Y <= Math.Max(start.Y, y);
                }).ToArray();
        }
        else
        {
            ids = session.Query(new Circle2(view.Pointer, .8)).Take(1).ToArray();
            if (ids.Length == 1 && lastClickId == ids[0] && total.Ticks - lastClickTicks < TimeSpan.TicksPerSecond / 3)
            {
                UnitKind kind = session.Units.Single(u => u.Id == ids[0]).Kind;
                ids = session.Units.Where(u => u.Kind == kind).Select(u => u.Id).ToArray();
            }
            lastClickId = ids.FirstOrDefault();
            lastClickTicks = total.Ticks;
            if (additive)
            {
                change = SelectionChange.Toggle;
            }
        }
        session.Dispatch(new SelectIntent(ids, change));
    }

    private void ContextOrder()
    {
        if (view.Placing)
        {
            view.Placing = false;
            return;
        }
        int[] ids = session.Selection.Snapshot().ToArray();
        ResourceNode? node = session.Resources.FirstOrDefault(r => (r.Position - view.Pointer).LengthSquared < 2 && r.Amount > 0);
        UnitOrder order = new MoveOrder(view.Pointer);
        if (node is not null)
        {
            order = new GatherOrder(node.Id);
        }
        else if ((StrategySession.EnemyPosition - view.Pointer).LengthSquared < 2)
        {
            order = new AttackOrder(300);
        }
        session.Dispatch(new OrderIntent(ids, order));
    }

    private void RunAction(string action, InputFrame frame)
    {
        switch (action)
        {
            case "Build":
                view.Placing = true;
                break;
            case "Recruit":
                session.Dispatch(new ProduceIntent(UnitKind.Worker));
                break;
            case "Ranger":
                session.Dispatch(new ProduceIntent(UnitKind.Ranger));
                break;
            case "Stop":
                session.Dispatch(new OrderIntent(session.Selection.Snapshot().ToArray(), new IdleOrder()));
                break;
            case "Idle":
                StrategyUnit? idle = session.Units.FirstOrDefault(u => u.Kind == UnitKind.Worker && u.Order is IdleOrder);
                if (idle is not null)
                {
                    session.Dispatch(new SelectIntent([idle.Id]));
                    view.Center(idle.Position);
                }
                break;
            case "Center":
                StrategyUnit? selected = session.Units.FirstOrDefault(u => session.Selection.Contains(u.Id));
                view.Center(selected?.Position ?? StrategySession.Home);
                break;
            case "Cancel":
                view.Placing = false;
                session.Dispatch(new SelectIntent([]));
                break;
            default:
                if (action.StartsWith("Group", StringComparison.Ordinal))
                {
                    session.Dispatch(new ControlGroupIntent(int.Parse(action.AsSpan(5)), frame.IsDown(Store)));
                }
                break;
        }
    }

    public void Dispose() => adapter.Dispose();
}
