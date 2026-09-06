using Aurelian.Composition;
using Aurelian.Machina;
using Aurelian.Rendering.Raster;
using Ariadne.OptFlow.Presentation;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Pipeline;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;

namespace Aurelian.Ariadne.VnDemo;

public sealed class VnMachinaLayer : IAurelianLayer
{
    public static readonly LayerId Id = new("machina-vn-ui");
    private const int Width = 1280;
    private const int Height = 720;
    private readonly RenApp app;
    private readonly VnUiSkin skin;
    private MachinaPreparedPresentation? prepared;
    private string? preparedKey;
    private byte[]? renderedRgba;
    private UiAction? pressedAction;

    public VnMachinaLayer(RenApp app, VnUiSkin? skin = null)
    {
        this.app = app;
        this.skin = skin ?? VnUiSkin.Load();
    }

    public byte[] Rgba8 => RenderOverlay();

    public VnUiSkin Skin => skin;

    public IReadOnlyList<global::Machina.Presentation.MachinaNineSlicePrimitive>? ProofNineSlices { get; set; }

    public IReadOnlyList<global::Machina.Presentation.MachinaProgrammablePanelPrimitive>? ProofPanels { get; set; }

    public bool SuppressOverlay { get; set; }

    public IReadOnlyList<global::Machina.Presentation.MachinaNineSlicePrimitive> NineSlices
    {
        get
        {
            EnsurePrepared();
            return ProofNineSlices ?? BuildNineSlices();
        }
    }

    public IReadOnlyList<global::Machina.Presentation.MachinaProgrammablePanelPrimitive> Panels
    {
        get
        {
            EnsurePrepared();
            return ProofPanels ?? BuildPanels();
        }
    }

    public LayerPoint ActionCenter(string actionName)
    {
        EnsurePrepared();
        KeyValuePair<global::Machina.Layout.Rows.NodeId, UiAction> entry = prepared!.Lowering.Actions
            .Single(pair => pair.Value.Name == actionName);
        Rect rect = prepared.Resolved.Nodes[entry.Key].Rect;
        return new LayerPoint(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
    }

    public LayerDescriptor Describe()
    {
        return new LayerDescriptor(
            Id,
            100,
            true,
            new LayerViewport(0, 0, Width, Height),
            LayerPresentationMode.DirectHostPass,
            LayerInputPolicy.Opaque);
    }

    public void Attach(LayerSurfaceDescriptor surface)
    {
        EnsurePrepared();
    }

    public void Resize(LayerSurfaceDescriptor surface)
    {
        preparedKey = null;
        renderedRgba = null;
    }

    public void Update(LayerUpdateContext context)
    {
        EnsurePrepared();
    }

    public LayerPresentationDto Present(LayerPresentationContext context)
    {
        EnsurePrepared();
        return new LayerPresentationDto(
            Id,
            Describe().Viewport,
            true,
            context.Surface.Kind,
            app.State.Screen.ToString());
    }

    public LayerInputResult HandleInput(LayerInputEvent input)
    {
        EnsurePrepared();
        switch (input)
        {
            case LayerKeyChanged { IsPressed: true, Key: LayerKey.ArrowUp }:
                app.Dispatch(new NavigateIntent(-1));
                return new LayerInputResult(true, RequestFocus: true);
            case LayerKeyChanged { IsPressed: true, Key: LayerKey.ArrowDown }:
                app.Dispatch(new NavigateIntent(1));
                return new LayerInputResult(true, RequestFocus: true);
            case LayerKeyChanged { IsPressed: true, Key: LayerKey.Enter or LayerKey.Space }:
                app.Dispatch(new ConfirmIntent());
                return new LayerInputResult(true, RequestFocus: true);
            case LayerKeyChanged { IsPressed: true, Key: LayerKey.Escape }:
                app.Dispatch(new BackIntent());
                return new LayerInputResult(true, RequestFocus: true);
            case LayerPointerButtonChanged pointer when pointer.Button == LayerPointerButton.Primary:
                return HandlePointer(pointer);
            default:
                return LayerInputResult.ConsumedOnly;
        }
    }

    public void Detach()
    {
        prepared = null;
        preparedKey = null;
        renderedRgba = null;
    }

    private LayerInputResult HandlePointer(LayerPointerButtonChanged pointer)
    {
        UiAction? hit = prepared!.HitTest
            .HitTest(new PointerPoint(pointer.Position.X, pointer.Position.Y))
            ?.Action;
        if (pointer.IsPressed)
        {
            pressedAction = hit;
            return new LayerInputResult(true, RequestFocus: true, RequestCapture: true);
        }

        UiAction? action = pressedAction;
        pressedAction = null;
        if (action is not null && hit?.Id == action.Id)
        {
            Dispatch(action);
        }

        return new LayerInputResult(true, RequestFocus: true, ReleaseCapture: true);
    }

    private void Dispatch(UiAction action)
    {
        string name = action.Name;
        if (name == "ren.advance")
        {
            app.Dispatch(new AdvanceDialogueIntent());
        }
        else if (name == "ren.pause")
        {
            app.Dispatch(new OpenPauseMenuIntent());
        }
        else if (name == "ren.save")
        {
            app.Dispatch(new OpenSaveMenuIntent());
        }
        else if (name == "ren.load")
        {
            app.Dispatch(new OpenLoadMenuIntent());
        }
        else if (name.StartsWith("ren.choice.", StringComparison.Ordinal))
        {
            app.Dispatch(new ChooseDialogueOptionIntent(name[11..]));
        }
        else if (name.StartsWith("ren.entry.", StringComparison.Ordinal))
        {
            app.Activate(name[10..]);
        }
    }

    private byte[] RenderOverlay()
    {
        EnsurePrepared();
        if (SuppressOverlay)
        {
            return new byte[Width * Height * 4];
        }
        if (renderedRgba is not null)
        {
            return renderedRgba;
        }

        RasterFrame frame = new AurelianCpuRasterRenderer().Render(
            MachinaPresentationTranslator.Translate(prepared!.PresentationFrame));
        Aurelian.Rendering.Contracts.Resolved2D.Resolved2DRgbaColor[] pixels =
            frame.Surface.CopyPixels();
        var rgba = new byte[pixels.Length * 4];
        for (int index = 0; index < pixels.Length; index++)
        {
            int offset = index * 4;
            rgba[offset] = pixels[index].R;
            rgba[offset + 1] = pixels[index].G;
            rgba[offset + 2] = pixels[index].B;
            rgba[offset + 3] = pixels[index].A;
        }

        renderedRgba = rgba;
        return renderedRgba;
    }

    private void EnsurePrepared()
    {
        RenPresentationSnapshot presentation = app.Presentation;
        string dialogueKey = presentation.Dialogue is null
            ? "none"
            : $"{presentation.Dialogue.OperationId}:{presentation.Dialogue.SelectedChoiceIndex}";
        string menuKey = string.Join('|', presentation.MenuEntries.Select(entry => entry.Label));
        string key = $"{presentation.Screen}|{presentation.SelectedItem}|{dialogueKey}|{menuKey}|{presentation.Notice}";
        if (prepared is not null && preparedKey == key)
        {
            return;
        }

        UiNode document = presentation.Screen == RenScreen.Game
            ? BuildGame(presentation)
            : BuildMenu(presentation);
        prepared = new MachinaPresentationPipeline().Prepare(document, Width, Height);
        preparedKey = key;
        renderedRgba = null;
    }

    private IReadOnlyList<global::Machina.Presentation.MachinaNineSlicePrimitive> BuildNineSlices()
    {
        var result = new List<global::Machina.Presentation.MachinaNineSlicePrimitive>();
        if (app.State.Screen == RenScreen.Game)
        {
            AddPanel(result, "dialogue-panel", "dialogue");
        }
        else
        {
            AddPanel(result, "menu-shadow", "dialogue");
        }

        return result;
    }

    private IReadOnlyList<global::Machina.Presentation.MachinaProgrammablePanelPrimitive> BuildPanels()
    {
        var result = new List<global::Machina.Presentation.MachinaProgrammablePanelPrimitive>();
        string nodeId = app.State.Screen == RenScreen.Game ? "dialogue-panel" : "menu-shadow";
        var id = new global::Machina.Layout.Rows.NodeId(nodeId);
        if (prepared!.Resolved.Nodes.TryGetValue(id, out global::Machina.Layout.Documents.ResolvedLayoutNode? node))
        {
            result.Add(skin.CreateProgrammable($"skin.{nodeId}", "dialogue", node.Rect));
        }
        return result;
    }

    private void AddPanel(
        ICollection<global::Machina.Presentation.MachinaNineSlicePrimitive> result,
        string nodeId,
        string panelId)
    {
        var id = new global::Machina.Layout.Rows.NodeId(nodeId);
        if (prepared!.Resolved.Nodes.TryGetValue(id, out global::Machina.Layout.Documents.ResolvedLayoutNode? node))
        {
            result.Add(skin.Create($"skin.{nodeId}", panelId, node.Rect));
        }
    }

    private static UiNode BuildMenu(RenPresentationSnapshot presentation)
    {
        var children = new List<UiNode>
        {
            UI.Anchor(
                UI.Rect(
                    id: "menu-shadow",
                    style: new UiStyle(
                        Background: ColorToken.Hex(0x00000000))),
                id: "menu-shadow-slot",
                left: 54,
                top: 48,
                width: 670,
                height: 624),
            UI.Anchor(
                UI.Text(
                    presentation.Title,
                    id: "title",
                    color: ColorToken.Hex(0xFFF4DEFF),
                    size: TextSize.H1),
                id: "title-slot",
                left: 92,
                top: 92,
                width: 560,
                height: 54),
            UI.Anchor(
                UI.Text(
                    presentation.Subtitle,
                    id: "subtitle",
                    color: ColorToken.Hex(0xF5A04CFF),
                    size: TextSize.Sm),
                id: "subtitle-slot",
                left: 94,
                top: 152,
                width: 560,
                height: 34),
        };

        for (int index = 0; index < presentation.MenuEntries.Count; index++)
        {
            RenMenuEntry entry = presentation.MenuEntries[index];
            bool selected = index == presentation.SelectedItem;
            children.Add(UI.Anchor(
                MenuButton(entry, selected),
                id: $"entry-{index}-slot",
                left: 92,
                top: 224 + (index * 72),
                width: 570,
                height: 56));
        }

        children.Add(UI.Anchor(
            UI.Text(
                presentation.Notice,
                id: "notice",
                color: ColorToken.Hex(0xD7C4A9FF),
                size: TextSize.Sm),
            id: "notice-slot",
            left: 94,
            bottom: 66,
            width: 560,
            height: 28));

        return UI.Surface(
            id: "sunkill-menu-surface",
            width: Width,
            height: Height,
            children: children);
    }

    private static UiNode BuildGame(RenPresentationSnapshot presentation)
    {
        DialoguePresentationSnapshot dialogue = presentation.Dialogue
            ?? throw new InvalidOperationException("The game screen requires dialogue presentation.");
        var children = new List<UiNode>
        {
            UI.Anchor(
                UI.Rect(
                    id: "dialogue-panel",
                    style: new UiStyle(
                        Background: ColorToken.Hex(0x00000000))),
                id: "dialogue-panel-slot",
                left: 44,
                right: 44,
                bottom: 30,
                height: 220),
        };

        string speaker = dialogue.SpeakerId switch
        {
            "oppenheimer" => "J. ROBERT OPPENHEIMER",
            "groves" => "GENERAL LESLIE GROVES",
            null or "" => "NARRATION",
            _ => dialogue.SpeakerId.ToUpperInvariant(),
        };
        children.Add(UI.Anchor(
            UI.Text(
                speaker,
                id: "speaker",
                color: ColorToken.Hex(0xFFC06BFF),
                size: TextSize.H1),
            id: "speaker-slot",
            left: 78,
            bottom: 194,
            width: 520,
            height: 38));

        string[] lines = Wrap(dialogue.Text, 76);
        for (int index = 0; index < lines.Length; index++)
        {
            children.Add(UI.Anchor(
                UI.Text(
                    lines[index],
                    id: $"body-{index}",
                    color: ColorToken.Hex(0xFFF9EFFF),
                    size: TextSize.Md),
                id: $"body-{index}-slot",
                left: 80,
                bottom: 146 - (index * 34),
                width: 950,
                height: 34));
        }

        for (int index = 0; index < dialogue.Choices.Count; index++)
        {
            DialoguePresentationChoice choice = dialogue.Choices[index];
            bool selected = index == dialogue.SelectedChoiceIndex;
            children.Add(UI.Anchor(
                StandardUI.Button(
                    $"{(selected ? "*" : " ")} {choice.Text}",
                    id: $"choice-{choice.Id}",
                    action: UiAction.Named($"ren.choice.{choice.Id}"),
                    style: ButtonStyle(selected, 840, 58, TextAlignX.Left)),
                id: $"choice-{choice.Id}-slot",
                left: 92,
                top: 330 + (index * 76),
                width: 840,
                height: 58));
        }

        (string Label, string Action)[] controls =
        [
            ("SAVE [F]", "ren.save"),
            ("LOAD [I]", "ren.load"),
            ("MENU [ESC]", "ren.pause"),
        ];
        for (int index = 0; index < controls.Length; index++)
        {
            children.Add(UI.Anchor(
                StandardUI.Button(
                    controls[index].Label,
                    id: $"control-{index}",
                    action: UiAction.Named(controls[index].Action),
                    style: ButtonStyle(false, 150, 38, TextAlignX.Center)),
                id: $"control-{index}-slot",
                right: 54,
                top: 40 + (index * 50),
                width: 150,
                height: 38));
        }

        children.Add(UI.Anchor(
            StandardUI.Button(
                "ADVANCE",
                id: "advance",
                action: UiAction.Named("ren.advance"),
                style: ButtonStyle(true, 164, 42, TextAlignX.Center)),
            id: "advance-slot",
            right: 70,
            bottom: 54,
            width: 164,
            height: 42));

        return UI.Surface(
            id: "sunkill-game-surface",
            width: Width,
            height: Height,
            children: children);
    }

    private static UiNode MenuButton(RenMenuEntry entry, bool selected)
    {
        return StandardUI.Button(
            $"{(selected ? "*" : " ")} {entry.Label}",
            id: $"entry-{entry.Id}",
            action: UiAction.Named($"ren.entry.{entry.Id}"),
            style: ButtonStyle(selected, 570, 56, TextAlignX.Left));
    }

    private static StandardButtonStyle ButtonStyle(
        bool selected,
        int width,
        int height,
        TextAlignX align)
    {
        return new StandardButtonStyle(
            Background: ColorToken.Hex(selected ? 0xA6421FF4 : 0x171923E8),
            Foreground: ColorToken.Hex(0xFFF8EBFF),
            BorderColor: ColorToken.Hex(selected ? 0xFFD072FF : 0x6D6257FF),
            BorderThickness: selected ? 2 : 1,
            TextStyle: new TextStyle(
                ColorToken.Hex(0xFFF8EBFF),
                TextSize.Md,
                align,
                TextAlignY.Center),
            Width: width,
            Height: height,
            CornerRadius: 8);
    }

    private static string[] Wrap(string text, int width)
    {
        var result = new List<string>();
        string current = "";
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + word.Length + 1 > width)
            {
                result.Add(current);
                current = word;
            }
            else
            {
                current = current.Length == 0 ? word : current + " " + word;
            }
        }

        if (current.Length > 0)
        {
            result.Add(current);
        }

        return result.Count == 0 ? [""] : result.ToArray();
    }
}

public sealed class VnImageSemanticLayer(LayerId id, int zOrder) : IAurelianLayer
{
    public LayerDescriptor Describe()
    {
        return new LayerDescriptor(
            id,
            zOrder,
            true,
            new LayerViewport(0, 0, 1280, 720),
            LayerPresentationMode.DirectHostPass,
            LayerInputPolicy.None);
    }

    public void Attach(LayerSurfaceDescriptor surface)
    {
    }

    public void Resize(LayerSurfaceDescriptor surface)
    {
    }

    public void Update(LayerUpdateContext context)
    {
    }

    public LayerPresentationDto Present(LayerPresentationContext context)
    {
        return new LayerPresentationDto(
            id,
            Describe().Viewport,
            true,
            context.Surface.Kind,
            id.Value);
    }

    public LayerInputResult HandleInput(LayerInputEvent input)
    {
        return LayerInputResult.Unconsumed;
    }

    public void Detach()
    {
    }
}
