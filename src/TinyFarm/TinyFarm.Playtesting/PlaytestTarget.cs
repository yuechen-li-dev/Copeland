using System.Globalization;
using Aurelian.Playtesting;
using Aurelian.Runtime.Inspection;
using Aurelian.Composition;
using Aurelian.GameHost;
using InputMan.Aurelian;
using InputMan.Core;
using Machina.Runtime.Input;
using TinyFarm.Core;
using TinyFarm.InputMan;
using TinyFarm.Native;
using TinyFarm.Runtime;

namespace TinyFarm.Playtesting;

public sealed record PlaytestProduct(string Actor, string Product, int Count);
public sealed record PlaytestItem(string Actor, string Item, string Name);
public sealed record PlaytestMenuTarget(string Action, double X, double Y);
public sealed record PlaytestAgent(string Id, string Name, string? Scene, int? X, int? Y,
    string? Kind, string? Control, string? ObjectPose, string[] Conditions, int? LastCollectionDay, int? LastCollectionCoins);
public sealed record PlaytestObservation(string Backend, string Screen, string Status, string SemanticHash, string FieldHash,
    int Minute, string Scene, int X, int Y, int Money, bool CapturesGameplay, bool Focused,
    bool HudVisible, string Search, bool SearchFocused, string[] ActionMaps,
    PlaytestProduct[] Products, PlaytestItem[] Items, PlaytestMenuTarget[] Targets,
    int Width, int Height, bool Quit, int AcceptedActions, int RejectedActions,
    int Health, int Spirit, int SpiritMaximum, PlaytestAgent[] Agents, AgentInspection[] Brains);
public sealed record PlaytestTrace(int Index, PlaytestStep Step, PlaytestObservation? Observation, string? Error);

/// <summary>Owned input injection: no SendInput, global hooks, or process-external mouse control.</summary>
public class PlaytestTarget : IPlaytestTarget<PlaytestObservation>, IDisposable
{
    private readonly TinyFarmNativeUi ui;
    private readonly Queue<LayerInputEvent> events = new();
    private readonly Dictionary<string, string> marks = new(StringComparer.Ordinal);
    private ulong sequence;
    private TimeSpan total;
    private LayerPoint pointer;
    private TinyFarmSession? inspectedSession;
    protected readonly TinyFarmGame Game;
    protected readonly AurelianInputAdapter Input;
    public int Width { get; protected set; }
    public int Height { get; protected set; }
    public bool Focused { get; protected set; } = true;
    public virtual string Backend => "headless-inputman-machina";
    public bool Quit => Game.ShouldQuit;

    public PlaytestTarget(TinyFarmGame game, AurelianInputAdapter input, int width = 1920, int height = 1080)
    {
        Game = game;
        Input = input;
        Width = width;
        Height = height;
        ui = new TinyFarmNativeUi(game);
        Input.SetContexts(Game.Contexts);
        EnableInspection();
    }

    public virtual void Queue(LayerInputEvent value) => events.Enqueue(value);

    public void Key(KeyboardKey key, bool down)
    {
        Input.RecordButton(Controls.Key(key), down);
        LayerInputEvent layer = Input.ToLayerEvent(key, down);
        Queue(layer);
    }

    public void Pointer(double x, double y, bool? down = null)
    {
        LayerPoint next = new((float)(x * Width), (float)(y * Height));
        Queue(new LayerPointerMoved(next, pointer));
        Input.RecordAxis(Controls.Mouse(MouseAxis.PositionX), (float)next.X);
        Input.RecordAxis(Controls.Mouse(MouseAxis.PositionY), (float)next.Y);
        Input.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), (float)(next.X - pointer.X));
        Input.RecordAxis(Controls.Mouse(MouseAxis.DeltaY), (float)(next.Y - pointer.Y));
        pointer = next;
        if (down is bool pressed)
        {
            Input.RecordButton(Controls.Mouse(MouseButton.Primary), pressed);
            Queue(new LayerPointerButtonChanged(pointer, LayerPointerButton.Primary, pressed));
        }
    }

    public void Text(string text) => Queue(new LayerTextEntered(text));

    public virtual void ReleaseMouse()
    {
        Input.RecordButton(Controls.Mouse(MouseButton.Primary), false);
        ui.ResetPointer();
    }

    public void Scroll(double x, double y, float delta)
    {
        Pointer(x, y);
        Input.RecordAxis(Controls.Mouse(MouseAxis.WheelY), delta);
        Queue(new LayerPointerWheel(pointer, 0, delta));
    }

    public virtual void Frame(TimeSpan elapsed)
    {
        EnableInspection();
        total += elapsed;
        Input.BeginFrame(new AurelianHostFrame(++sequence, elapsed, total));
        TinyFarmInputPump.Step(Game, Input, elapsed, Focused, () =>
        {
            while (events.TryDequeue(out LayerInputEvent? value))
            {
                if (Focused) TinyFarmMenuInputRouter.Handle(Game, ui, value, ToMenuPoint);
            }
        });
        Game.PendingAudio.Clear();
        EnableInspection();
    }

    protected void EnableInspection()
    {
        if (!ReferenceEquals(inspectedSession, Game.Host.Session))
        {
            inspectedSession = Game.Host.Session;
            inspectedSession.EnableAgentInspection();
        }
    }

    private PointerPoint ToMenuPoint(LayerPoint point)
    {
        var layout = new TinyFarmPresentationLayout(Width, Height);
        return new PointerPoint((point.X - layout.UiLeft) / layout.UiScale,
            (point.Y - layout.UiTop) / layout.UiScale);
    }

    public virtual void Focus(bool focused)
    {
        Focused = focused;
        Input.OnFocusChanged(focused);
        events.Clear();
        ui.ResetPointer();
        if (!focused) Game.Menus.SearchFocused = false;
    }

    public virtual void Resize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public virtual void Capture(string path)
    {
        throw new NotSupportedException("PNG captures require --native. Headless runs produce semantic observations.");
    }

    public virtual (double X, double Y) ActionPoint(string action)
    {
        PointerPoint point = ui.ActionCenter(TinyFarmFrameProjector.Project(Game.State, Game.Definitions), action);
        var layout = new TinyFarmPresentationLayout(Width, Height);
        return ((layout.UiLeft + point.X * layout.UiScale) / Width,
            (layout.UiTop + point.Y * layout.UiScale) / Height);
    }

    public void SettlePersistence()
    {
        long start = Environment.TickCount64;
        while (Game.SaveInProgress || Game.LoadInProgress)
        {
            if (Environment.TickCount64 - start > 10000)
            {
                throw new TimeoutException("Persistence did not settle within 10 seconds.");
            }
            Frame(TimeSpan.Zero);
            Thread.Sleep(1);
        }
    }

    public void Command(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) throw new FormatException("Command is empty.");
        string verb = words[0].ToLowerInvariant();
        if (command.Equals("inspect", StringComparison.OrdinalIgnoreCase)
            || command.Equals("inspect brains", StringComparison.OrdinalIgnoreCase)) return;
        if (command.Equals("new game", StringComparison.OrdinalIgnoreCase)) Game.Start();
        else if (command.Equals("open inventory", StringComparison.OrdinalIgnoreCase)) Game.OpenInventory(false);
        else if (command.Equals("open stats", StringComparison.OrdinalIgnoreCase)) Game.OpenStats(false);
        else if (command.Equals("open pause", StringComparison.OrdinalIgnoreCase))
        {
            Game.OpenPause();
        }
        else if (verb == "close" && words.Length == 1) Game.BackFromMenu();
        else if (verb == "ui" && words.Length == 2)
        {
            if (!ui.ActionNames(TinyFarmFrameProjector.Project(Game.State, Game.Definitions)).Contains(words[1]))
            {
                throw new FormatException("Action is not present in this menu: " + words[1]);
            }
            Game.DispatchMenu(words[1]);
        }
        else if (verb is "save" or "load" && words.Length is 1 or 2)
        {
            string? slot = words.Length == 2 ? words[1] : null;
            if (slot is not null && (slot.Length > 80 || slot.Contains("..", StringComparison.Ordinal)
                || slot.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_')))
            {
                throw new FormatException("Save slot must be a simple name, such as slot2.sav; use --save-dir for its directory.");
            }
            bool succeeded = verb == "save" ? Game.Save(slot) : Game.Load(slot);
            if (!succeeded) throw new InvalidOperationException(Game.Status);
        }
        else if (verb == "mark" && words.Length == 2) marks[words[1]] = StateHash();
        else if (verb == "assert") Assert(words);
        else
        {
            GameIntent intent;
            if (verb == "open" && words.Length == 3 && words[1] == "container")
            {
                intent = new OpenContainerIntent(new ActorId(words[2]));
            }
            else if (verb == "sleep" && words.Length == 1) intent = new SleepIntent();
            else if (verb == "sword" && words.Length == 1) intent = new SwordIntent();
            else if (verb == "eat" && words.Length is 1 or 2)
            {
                intent = new EatIntent(words.Length == 2 ? new ProductId(words[1]) : null);
            }
            else if (verb is "deposit" or "withdraw" && words.Length is 2 or 3)
            {
                ActorId chest = Game.OpenContainer ?? throw new InvalidOperationException("Open a nearby container first.");
                int count = words.Length == 3 ? int.Parse(words[2], CultureInfo.InvariantCulture) : 1;
                if (words[1].StartsWith("item:", StringComparison.Ordinal))
                {
                    intent = new TransferContainerIntent(chest, verb == "deposit", count, Item: new ItemId(words[1][5..]));
                }
                else if (words[1].StartsWith("product:", StringComparison.Ordinal))
                {
                    intent = new TransferContainerIntent(chest, verb == "deposit", count, Product: new ProductId(words[1][8..]));
                }
                else throw new FormatException("Use deposit/withdraw item:ID or product:ID [count].");
            }
            else intent = TinyFarmCommandParser.Parse(command.StartsWith("intent ", StringComparison.Ordinal) ? command[7..] : command);
            int rejectedBefore = Game.RejectedActions;
            Game.Execute(intent);
            if (Game.RejectedActions > rejectedBefore) throw new InvalidOperationException(Game.Status);
        }
        Input.SetContexts(Game.Contexts);
    }

    private void Assert(string[] words)
    {
        if (words.Length == 5 && words[1] == "product")
        {
            int expected = int.Parse(words[4], CultureInfo.InvariantCulture);
            int actual = Game.State.ProductCount(new ActorId(words[2]), new ProductId(words[3]));
            if (expected != actual)
            {
                throw new InvalidOperationException($"Expected {words[2]} to own {expected} {words[3]}, found {actual}.");
            }
            return;
        }
        bool valid = words.Length == 3 && words[1] switch
        {
            "screen" => Game.Screen.ToString().Equals(words[2], StringComparison.OrdinalIgnoreCase),
            "unchanged" => marks.TryGetValue(words[2], out string? hash) && hash == StateHash(),
            "money" => Game.State.Actor(TinyFarmIds.Player).Money == int.Parse(words[2], CultureInfo.InvariantCulture),
            "search" => Game.Menus.Search == words[2],
            _ => false
        };
        if (!valid) throw new InvalidOperationException("Assertion failed: " + string.Join(' ', words));
    }

    private string StateHash() => TinyFarmSemanticHash.Compute(Game.State) + ":" + Game.Host.Session.Field.SemanticHash;

    public PlaytestObservation Observe()
    {
        EnableInspection();
        ActorSceneState player = Game.State.ActorScene(TinyFarmIds.Player);
        PlaytestMenuTarget[] targets = [];
        if (Game.CapturesGameplay && !Game.Dialogue.IsActive)
        {
            targets = ui.ActionNames(TinyFarmFrameProjector.Project(Game.State, Game.Definitions))
                .Select(action =>
                {
                    (double x, double y) = ActionPoint(action);
                    return new PlaytestMenuTarget(action, x, y);
                }).ToArray();
        }
        return new PlaytestObservation(Backend, Game.Screen.ToString(), Game.Status, TinyFarmSemanticHash.Compute(Game.State),
            Game.Host.Session.Field.SemanticHash,
            Game.State.Minute, player.Scene.Value, player.WorldPosition.XUnits, player.WorldPosition.YUnits,
            Game.State.Actor(TinyFarmIds.Player).Money, Game.CapturesGameplay, Focused, Game.Presentation.HudVisible,
            Game.Menus.Search, Game.Menus.SearchFocused, Game.Contexts.Select(map => map.ToString()).ToArray(),
            Game.State.InventoryStacks.Select(stack => new PlaytestProduct(stack.Actor.Value, stack.Product.Value, stack.Count)).ToArray(),
            Game.State.Actors.SelectMany(actor => actor.Inventory.Select(id => new PlaytestItem(actor.Id.Value, id.Value, Game.State.Item(id).Name))).ToArray(),
            targets, Width, Height, Game.ShouldQuit, Game.AcceptedActions, Game.RejectedActions,
            Game.State.Slice?.Health ?? Game.State.Actor(TinyFarmIds.Player).Agent?.Health?.Current ?? 0,
            Game.State.Actor(TinyFarmIds.Player).Rpg?.SpiritCurrent ?? 0,
            Game.State.Actor(TinyFarmIds.Player).Rpg?.SpiritMaximum ?? 0,
            Game.State.Actors.Select(actor =>
            {
                ActorSceneState? placement = Game.State.ActorScenes.FirstOrDefault(value => value.Actor == actor.Id);
                return new PlaytestAgent(actor.Id.Value, actor.Name, placement?.Scene.Value,
                    placement?.WorldPosition.XUnits, placement?.WorldPosition.YUnits,
                    actor.Agent?.Kind.ToString(), actor.Agent?.Control.ToString(), actor.Agent?.ObjectPose?.ToString(),
                    actor.Agent?.Conditions.ToArray() ?? [], actor.Agent?.Container?.LastCollectionDay,
                    actor.Agent?.Container?.LastCollectionCoins);
            }).ToArray(), Game.Host.Session.InspectAgents());
    }

    public virtual void Dispose() => Input.Dispose();
}


