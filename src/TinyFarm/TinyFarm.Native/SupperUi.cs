using System.Security.Cryptography;
using Aurelian.GameWorld2D;
using Aurelian.Machina;
using Aurelian.Rendering.Raster;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Pipeline;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal sealed class SupperUi(TinyFarmSupperGame game)
{
    private SupperUiKey? key;
    private SpriteAtlasResource? resource;
    private string? clockKey;
    private SpriteAtlasResource? clockResource;
    private string? promptKey;
    private SpriteAtlasResource? promptResource;
    public int Rebuilds { get; private set; }

    public SpriteAtlasResource Resource(TinyFarmFrame frame)
    {
        SupperUiKey next = CreateKey(frame);
        if (key == next && resource is not null)
        {
            return resource;
        }
        key = next;
        Rebuilds++;
        resource = Render("supper-ui", Build(frame), 1280, 720);
        return resource;
    }

    public SupperUiResources Resources(TinyFarmFrame frame)
    {
        return new SupperUiResources(Resource(frame), ClockResource(frame), PromptResource(frame));
    }

    private SpriteAtlasResource ClockResource(TinyFarmFrame frame)
    {
        string next = frame.CurrentLocationName + "\n" + frame.Time;
        if (clockKey == next && clockResource is not null)
        {
            return clockResource;
        }
        clockKey = next;
        var nodes = new List<UiNode>();
        Text(nodes, "clock", $"{frame.CurrentLocationName}  /  {frame.Time}", 0, 0, 400, TextSize.Md);
        clockResource = Render("supper-clock", UI.Surface(id: "clock-surface", width: 400, height: 34, children: nodes), 400, 34);
        return clockResource;
    }

    private SpriteAtlasResource? PromptResource(TinyFarmFrame frame)
    {
        InteractionTarget? target = TinyFarmSpatialQueries.SelectInteractionTarget(
            game.State,
            TinyFarmIds.Player,
            game.Definitions.Scenes);
        string? prompt = target is null || game.CapturesGameplay
            ? null
            : target.Kind switch
            {
                InteractionTargetKind.Actor => "E  Talk to " + game.State.Actor(target.Actor!.Value).Name,
                InteractionTargetKind.Plot => "1 + SPACE plant   /   E tend or harvest",
                InteractionTargetKind.Enemy => "4 + SPACE  Shoo the slime",
                InteractionTargetKind.Tree => "3 + SPACE  Chop firewood",
                InteractionTargetKind.GroundItem => "E  Pick up wild mint",
                InteractionTargetKind.ForageNode => "E  Gather mushrooms",
                InteractionTargetKind.CookingStation => "E  Cook supper",
                InteractionTargetKind.Portal => "E  " + frame.SceneRoutes!.Single(route => route.TriggerObject == target.SceneObject).InteractionLabel,
                _ => "E  Interact",
            };
        if (prompt is null)
        {
            return null;
        }
        if (promptKey == prompt && promptResource is not null)
        {
            return promptResource;
        }
        promptKey = prompt;
        var nodes = new List<UiNode>();
        Panel(nodes, "prompt", 0, 0, 710, 38, 0x203D32EE);
        Text(nodes, "prompt-text", prompt, 22, 8, 670, TextSize.Md, 0xFFF0BEFF);
        promptResource = Render("supper-prompt", UI.Surface(id: "prompt-surface", width: 710, height: 38, children: nodes), 710, 38);
        return promptResource;
    }

    private static SpriteAtlasResource Render(string id, UiNode node, int width, int height)
    {
        MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(node, width, height);
        RasterFrame raster = new AurelianCpuRasterRenderer().Render(MachinaPresentationTranslator.Translate(prepared.PresentationFrame));
        byte[] rgba = raster.Surface.CopyRgba8();
        return new SpriteAtlasResource(
            new SpriteAssetId(id),
            Convert.ToHexString(SHA256.HashData(rgba)),
            (uint)width,
            (uint)height,
            rgba,
            SpriteSampling.Linear);
    }

    private SupperUiKey CreateKey(TinyFarmFrame frame)
    {
        int objectives = 0;
        if (game.State.Facts.Contains(WorldFact.SupperSeedPlanted))
        {
            objectives |= 1;
        }
        if (game.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.SauteedHenOfTheWoods) > 0)
        {
            objectives |= 2;
        }
        if (game.State.Enemy(TinyFarmIds.DungeonSlime).Lifecycle == EnemyLifecycle.Defeated)
        {
            objectives |= 4;
        }
        if (game.State.Item(TinyFarmIds.WildMint).Owner is not null)
        {
            objectives |= 8;
        }
        if (TinyFarmSupper.IsComplete(game.State))
        {
            objectives |= 16;
        }

        var inventoryHash = new HashCode();
        foreach (TinyFarmInventoryView item in frame.Inventory)
        {
            inventoryHash.Add(item.Id, StringComparer.Ordinal);
            inventoryHash.Add(item.Count);
        }

        return new SupperUiKey(
            game.Screen,
            game.Status,
            frame.ActiveScene,
            game.State.SelectedHotbarSlot,
            objectives,
            game.Dialogue.Presentation?.OperationId,
            game.Dialogue.SelectedChoiceIndex,
            inventoryHash.ToHashCode());
    }

    private UiNode Build(TinyFarmFrame frame)
    {
        List<UiNode> nodes = [];
        Panel(nodes, "header", 22, 18, 1236, 74);
        Text(nodes, "title", "TINYFARM", 44, 29, 230, TextSize.H1, 0xEDD6A0FF);
        Text(nodes, "subtitle", "A LITTLE MINT OF KINDNESS", 245, 45, 510, TextSize.Md);

        Panel(nodes, "journal", 946, 110, 312, 457);
        Text(nodes, "journal-title", "SUPPER AT HOME", 965, 132, 275, TextSize.Md, 0xEDD6A0FF);
        int y = 185;
        foreach (string objective in game.Objectives())
        {
            Lines(nodes, "objective-" + y, objective, 967, y, 267, 22, TextSize.Md);
            y += 67;
        }
        ActorSceneState mara = game.State.ActorScene(TinyFarmIds.Mara);
        Text(nodes, "mara-location", "Mara: " + game.Definitions.Scenes.Get(mara.Scene).Name, 966, 535, 270, TextSize.Md, 0xEDD6A0FF);

        Panel(nodes, "footer", 22, 582, 1236, 120);
        string[] slots = ["1  SEEDS", "2  TURNIP", "3  AXE", "4  SWORD"];
        for (int index = 0; index < slots.Length; index++)
        {
            bool selected = game.State.SelectedHotbarSlot == index + 1;
            Panel(nodes, "slot-" + index, 42 + index * 155, 598, 145, 38, selected ? 0x52775EFFu : 0x263F38FFu);
            Text(nodes, "slot-label-" + index, slots[index], 54 + index * 155, 606, 127, TextSize.Md,
                selected ? 0xFFF0BEFF : 0xD8E3D6FF);
        }
        Text(nodes, "controls", "WASD move  E interact  SPACE tool  I bag", 690, 604, 540, TextSize.Md);
        Text(nodes, "save-controls", "ESC pause   F save   N load", 690, 634, 490, TextSize.Md, 0xEDD6A0FF);
        Text(nodes, "status", game.Status.Length > 97 ? game.Status[..94] + "..." : game.Status, 43, 668, 1180, TextSize.Md);

        if (!game.CapturesGameplay)
        {
            float scale = Math.Min(870f / frame.SceneWidth, 416f / frame.SceneHeight);
            float left = 475 - frame.SceneWidth * scale / 2;
            float top = 315 - frame.SceneHeight * scale / 2;
            foreach (TinyFarmSceneObjectView portal in frame.SceneObjects!.Where(item => item.Kind == SceneObjectKind.Portal))
            {
                string label = portal.Id.Value switch
                {
                    "farm-exit" => "TRAIL",
                    "residence-entrance" => "HEARTH HOUSE",
                    "dungeon-entrance" => "OLD BURROW",
                    "farm-entrance" => "FARM",
                    "town-entrance" => "TOWN",
                    "riverside-entrance" => "RIVER",
                    _ => "EXIT"
                };
                int x = Math.Clamp((int)(left + portal.Position.X * scale - 35), 40, 765);
                int py = Math.Clamp((int)(top + portal.Position.Y * scale - 25), 110, 505);
                Text(nodes, "sign-" + portal.Id.Value, label, x, py, 180, TextSize.Md, 0xFFF0BEFF);
            }
        }

        if (game.Dialogue.Presentation is { } dialogue)
        {
            Panel(nodes, "dialogue", 54, 329, 1172, 241, 0x142F29FA);
            Text(nodes, "speaker", "MARA  /  a neighbour, and a very good cook", 82, 346, 1000, TextSize.Md, 0xEDD6A0FF);
            Lines(nodes, "dialogue-body", dialogue.Text, 82, 390, 1080, 96, TextSize.Md);
            for (int index = 0; index < dialogue.Choices.Count; index++)
            {
                bool selected = dialogue.SelectedChoiceIndex == index;
                Text(nodes, "choice-" + index, (selected ? ">  " : "    ") + dialogue.Choices[index].Text,
                    100, 449 + index * 31, 1040, TextSize.Md, selected ? 0xFFF0BEFF : 0xCEDCCFFF);
            }
            Text(nodes, "dialogue-hint", "SPACE / ENTER next     UP / DOWN choose     ESC leave     F save", 82, 531, 1060, TextSize.Md);
        }
        else if (game.Screen != SupperScreen.Playing)
        {
            Panel(nodes, "modal-shade", 0, 0, 1280, 720, 0x102D25B0);
            Panel(nodes, "modal", 193, 133, 894, 436, 0x163D31FC);
            string title = game.Screen switch
            {
                SupperScreen.Title => "A LITTLE MINT OF KINDNESS",
                SupperScreen.Complete => "SUPPER IS READY",
                SupperScreen.Inventory => "YOUR POCKETS",
                _ => "TAKE A BREATHER"
            };
            Text(nodes, "modal-title", title, 233, 167, 800, TextSize.H1, 0xEDD6A0FF);
            string body = game.Screen switch
            {
                SupperScreen.Title => "A seed for tomorrow. A meal for today.\nHelp Mara make a little corner of the world feel like home.\nPlant, forage, cook - and discourage one uninvited slime.\nA small afternoon adventure. No timer. No grinding.",
                SupperScreen.Complete => "The stove is warm. The burrow is quiet.\nMara has set another place at the table: yours.\nYou finished this little afternoon. Thank you for playing.\nSave your home, or stay a little longer.",
                SupperScreen.Inventory => string.Join('\n', frame.Inventory.Select(item => $"{item.Name}  x{item.Count}")),
                _ => "Your afternoon is paused.\nWASD move / face objects. E interacts.\n1 seeds, 3 axe, 4 sword. SPACE uses the selected tool.\nFollow doorway signs. The journal keeps track of supper."
            };
            int lineY = 232;
            foreach (string line in body.Split('\n'))
            {
                Text(nodes, "modal-line-" + lineY, line, 235, lineY, 800, TextSize.Md);
                lineY += 35;
            }
            Text(nodes, "modal-action", game.Screen == SupperScreen.Title ? "ENTER  Begin your afternoon" : "ENTER  Back to the farm", 235, 460, 790, TextSize.Md, 0xEDD6A0FF);
            string secondary = game.Screen == SupperScreen.Title
                ? "N  Continue saved game     Q  Quit"
                : "F  Save     N  Continue saved game     Q  Quit";
            Text(nodes, "modal-secondary", secondary, 235, 507, 790, TextSize.Md);
            if (game.Status.StartsWith("Could not", StringComparison.Ordinal))
            {
                Text(nodes, "modal-error", game.Status, 235, 538, 800, TextSize.Md, 0xFFD1A0FF);
            }
        }
        return UI.Surface(id: "supper", width: 1280, height: 720, children: nodes);
    }

    private static void Panel(List<UiNode> nodes, string id, int x, int y, int width, int height, uint color = 0x163D31F5)
    {
        nodes.Add(UI.Anchor(UI.Rect(id: id, style: new UiStyle(Background: ColorToken.Hex(color),
            BorderColor: ColorToken.Hex(0x8DA48180), BorderThickness: 1, Shape: UiShapeKind.RoundedRect, CornerRadius: 12)),
            id: id + "-anchor", left: x, top: y, width: width, height: height));
    }

    private static void Text(List<UiNode> nodes, string id, string text, int x, int y, int width, TextSize size, uint color = 0xE7EBDDFF)
    {
        nodes.Add(UI.Anchor(UI.Text(text, id: id, color: ColorToken.Hex(color), size: size),
            id: id + "-anchor", left: x, top: y, width: width, height: 34));
    }

    private static void Lines(List<UiNode> nodes, string id, string text, int x, int y, int width, int characters, TextSize size)
    {
        string line = "";
        int row = 0;
        foreach (string word in text.Split(' '))
        {
            if (line.Length + word.Length > characters && line.Length > 0)
            {
                Text(nodes, id + row, line, x, y + row * 25, width, size);
                row++;
                line = "";
            }
            line += (line.Length == 0 ? "" : " ") + word;
        }
        Text(nodes, id + row, line, x, y + row * 25, width, size);
    }
}

internal readonly record struct SupperUiKey(
    SupperScreen Screen,
    string Status,
    SceneId? ActiveScene,
    int SelectedHotbarSlot,
    int Objectives,
    string? DialogueOperationId,
    int DialogueSelectedChoiceIndex,
    int InventoryHash);

internal readonly record struct SupperUiResources(
    SpriteAtlasResource Base,
    SpriteAtlasResource Clock,
    SpriteAtlasResource? Prompt);
