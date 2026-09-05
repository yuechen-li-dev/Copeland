using Aurelian.Composition;
using Aurelian.Machina;
using Aurelian.Rendering.Raster;
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
    public static readonly LayerId Id = new("machina-dialogue-overlay");
    private const int Width = 1280;
    private const int Height = 720;
    private readonly VnSession session;
    private MachinaPreparedPresentation? prepared;
    private string? preparedKey;
    private UiAction? pressedAction;

    public VnMachinaLayer(VnSession session)
    {
        this.session = session;
    }

    public int GameplayLeakCount { get; private set; }
    public byte[] Rgba8 => RenderOverlay();

    public LayerPoint ActionCenter(string actionName)
    {
        EnsurePrepared();
        KeyValuePair<global::Machina.Layout.Rows.NodeId, UiAction> entry = prepared!.Lowering.Actions
            .Single(pair => pair.Value.Name == actionName);
        Rect rect = prepared.Resolved.Nodes[entry.Key].Rect;
        return new LayerPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
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

    public void Attach(LayerSurfaceDescriptor surface) => EnsurePrepared();
    public void Resize(LayerSurfaceDescriptor surface) => preparedKey = null;
    public void Update(LayerUpdateContext context) => EnsurePrepared();

    public LayerPresentationDto Present(LayerPresentationContext context)
    {
        EnsurePrepared();
        return new LayerPresentationDto(Id, Describe().Viewport, true, context.Surface.Kind, session.Presentation.StepId);
    }

    public LayerInputResult HandleInput(LayerInputEvent input)
    {
        EnsurePrepared();
        switch (input)
        {
            case LayerKeyChanged { IsPressed: true, Key: LayerKey.ArrowUp }:
                session.MoveChoice(-1);
                return new LayerInputResult(true, RequestFocus: true);
            case LayerKeyChanged { IsPressed: true, Key: LayerKey.ArrowDown }:
                session.MoveChoice(1);
                return new LayerInputResult(true, RequestFocus: true);
            case LayerKeyChanged { IsPressed: true, Key: LayerKey.Enter or LayerKey.Space }:
                session.Advance();
                return new LayerInputResult(true, RequestFocus: true);
            case LayerKeyChanged { IsPressed: true, Key: LayerKey.Escape }:
                session.Cancel();
                return new LayerInputResult(true, RequestFocus: true);
            case LayerPointerButtonChanged pointer when pointer.Button == LayerPointerButton.Primary:
                return HandlePointer(pointer);
            default:
                GameplayLeakCount++;
                return LayerInputResult.ConsumedOnly;
        }
    }

    public void Detach()
    {
        prepared = null;
        preparedKey = null;
    }

    private LayerInputResult HandlePointer(LayerPointerButtonChanged pointer)
    {
        UiAction? hit = prepared!.HitTest.HitTest(new PointerPoint(pointer.Position.X, pointer.Position.Y))?.Action;
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
        if (name == "vn.advance") session.Advance();
        else if (name == "vn.control.0") session.RequestSave();
        else if (name == "vn.control.1") session.RequestLoad();
        else if (name == "vn.auto") session.ToggleAuto();
        else if (name == "vn.skip") session.ToggleSkip();
        else if (name.StartsWith("vn.choice.", StringComparison.Ordinal)) session.Choose(name[10..]);
    }

    private byte[] RenderOverlay()
    {
        EnsurePrepared();
        RasterFrame frame = new AurelianCpuRasterRenderer().Render(
            MachinaPresentationTranslator.Translate(prepared!.PresentationFrame));
        Aurelian.Rendering.Contracts.Resolved2D.Resolved2DRgbaColor[] pixels = frame.Surface.CopyPixels();
        var rgba = new byte[pixels.Length * 4];
        for (int index = 0; index < pixels.Length; index++)
        {
            int offset = index * 4;
            rgba[offset] = pixels[index].R;
            rgba[offset + 1] = pixels[index].G;
            rgba[offset + 2] = pixels[index].B;
            rgba[offset + 3] = pixels[index].A;
        }
        return rgba;
    }

    private void EnsurePrepared()
    {
        DialoguePresentation presentation = session.Presentation;
        string key = $"{presentation.StepId}|{presentation.SelectedChoiceIndex}|{presentation.AutoEnabled}|{presentation.SkipEnabled}";
        if (prepared is not null && preparedKey == key)
        {
            return;
        }
        prepared = new MachinaPresentationPipeline().Prepare(Build(presentation), Width, Height);
        preparedKey = key;
    }

    private static UiNode Build(DialoguePresentation presentation)
    {
        var children = new List<UiNode>
        {
            UI.Anchor(
                UI.Rect(
                    id: "dialogue-panel",
                    style: new UiStyle(
                        Background: ColorToken.Hex(0x101522E8),
                        BorderColor: ColorToken.Hex(0xD04A5BFF),
                        BorderThickness: 2,
                        Shape: UiShapeKind.RoundedRect,
                        CornerRadius: 18)) with
                {
                    Semantics = new UiSemantics(UiRole.Container, "Dialogue panel"),
                },
                id: "dialogue-panel-slot",
                left: 48,
                right: 48,
                bottom: 34,
                height: 218),
        };

        string speaker = string.IsNullOrWhiteSpace(presentation.Speaker) ? "NARRATION" : presentation.Speaker.ToUpperInvariant();
        children.Add(UI.Anchor(
            UI.Text(speaker, id: "speaker", color: ColorToken.Hex(0xFFBAC3FF), size: TextSize.H1),
            id: "speaker-slot",
            left: 82,
            bottom: 196,
            width: 420,
            height: 34));

        string[] lines = Wrap(presentation.Text, 74);
        for (int index = 0; index < lines.Length; index++)
        {
            children.Add(UI.Anchor(
                UI.Text(lines[index], id: $"body-{index}", color: ColorToken.Hex(0xF8F5F0FF), size: TextSize.Md),
                id: $"body-{index}-slot",
                left: 82,
                bottom: 148 - index * 34,
                width: 930,
                height: 34));
        }

        for (int index = 0; index < presentation.Choices.Count; index++)
        {
            DialoguePresentationChoice choice = presentation.Choices[index];
            bool selected = index == presentation.SelectedChoiceIndex;
            children.Add(UI.Anchor(
                StandardUI.Button(
                    $"{(selected ? ">" : " ")} {choice.Text}",
                    id: $"choice-{choice.Id}",
                    action: UiAction.Named($"vn.choice.{choice.Id}"),
                    style: new StandardButtonStyle(
                        Background: ColorToken.Hex(selected ? 0xA83248F2 : 0x293044E8),
                        Foreground: ColorToken.Hex(0xFFFFFFFF),
                        BorderColor: ColorToken.Hex(selected ? 0xFFD3DAFF : 0x737D96FF),
                        BorderThickness: selected ? 2 : 1,
                        TextStyle: new TextStyle(ColorToken.Hex(0xFFFFFFFF), TextSize.Md, TextAlignX.Left, TextAlignY.Center),
                        Width: 700,
                        Height: 52,
                        CornerRadius: 10)),
                id: $"choice-{choice.Id}-slot",
                left: 132,
                top: 322 + index * 66,
                width: 700,
                height: 52));
        }

        string[] controls = ["SAVE [F]", "LOAD [I]", presentation.AutoEnabled ? "AUTO ON" : "AUTO", presentation.SkipEnabled ? "SKIP ON" : "SKIP"];
        for (int index = 0; index < controls.Length; index++)
        {
            string action = index switch { 2 => "vn.auto", 3 => "vn.skip", _ => $"vn.control.{index}" };
            children.Add(UI.Anchor(
                StandardUI.Button(
                    controls[index],
                    id: $"control-{index}",
                    action: UiAction.Named(action),
                    style: new StandardButtonStyle(
                        Background: ColorToken.Hex(0x151B28D8),
                        Foreground: ColorToken.Hex(0xEFF2F8FF),
                        BorderColor: ColorToken.Hex(0x626D86FF),
                        BorderThickness: 1,
                        TextStyle: new TextStyle(ColorToken.Hex(0xEFF2F8FF), TextSize.Sm, TextAlignX.Center, TextAlignY.Center),
                        Width: 132,
                        Height: 36,
                        CornerRadius: 8)),
                id: $"control-{index}-slot",
                right: 66,
                top: 42 + index * 48,
                width: 132,
                height: 36));
        }

        children.Add(UI.Anchor(
            StandardUI.Button(
                "ADVANCE  >",
                id: "advance",
                action: UiAction.Named("vn.advance"),
                style: new StandardButtonStyle(
                    ColorToken.Hex(0xA83248F2),
                    ColorToken.Hex(0xFFFFFFFF),
                    ColorToken.Hex(0xFFD3DAFF),
                    1,
                    new TextStyle(ColorToken.Hex(0xFFFFFFFF), TextSize.Sm, TextAlignX.Center, TextAlignY.Center),
                    158,
                    42,
                    9)),
            id: "advance-slot",
            right: 76,
            bottom: 58,
            width: 158,
            height: 42));

        return UI.Surface(id: "vn-surface", width: Width, height: Height, children: children);
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
        if (current.Length > 0) result.Add(current);
        return result.Count == 0 ? [""] : result.ToArray();
    }
}

public sealed class VnImageSemanticLayer(LayerId id, int zOrder) : IAurelianLayer
{
    public LayerDescriptor Describe() => new(id, zOrder, true, new LayerViewport(0, 0, 1280, 720), LayerPresentationMode.DirectHostPass, LayerInputPolicy.None);
    public void Attach(LayerSurfaceDescriptor surface) { }
    public void Resize(LayerSurfaceDescriptor surface) { }
    public void Update(LayerUpdateContext context) { }
    public LayerPresentationDto Present(LayerPresentationContext context) => new(id, Describe().Viewport, true, context.Surface.Kind, id.Value);
    public LayerInputResult HandleInput(LayerInputEvent input) => LayerInputResult.Unconsumed;
    public void Detach() { }
}
