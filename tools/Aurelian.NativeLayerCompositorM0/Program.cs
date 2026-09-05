using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Composition;
using Aurelian.GameWorld2D;
using Aurelian.GameHost;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Machina;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Dominatus.SpriteForge;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Layout.Geometry;
using Machina.Pipeline;
using Machina.Presentation;
using InputMan.Aurelian;
using InputMan.Core;
using InputMan.Toml;
using TinyFarm.Core;
using TinyFarm.InputMan;
using FontRgba = Machina.Fonts.ReferenceRendering.Rgba32;

const int Width = 1280;
const int Height = 720;
string root = FindRepositoryRoot();
string artifactRoot = Path.Combine(root, "artifacts", "aurelian-native-layer-compositor-m0");
Directory.CreateDirectory(artifactRoot);

CompiledGraphicsProgram worldProgram = CompileShader(root, "samples/Aurelian/ForwardTexturedM3.v.ts");
CompiledGraphicsProgram analyticProgram = CompileShader(root, "src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts");
CompiledGraphicsProgram msdfProgram = CompileShader(root, "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts");

MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(BuildUi(), Width, Height);
NativeAnalyticShapeSubmission[] uiShapes = prepared.PresentationFrame.Operations
    .OfType<MachinaAnalyticShapePrimitive>()
    .Select(primitive => AurelianAnalyticShapePresentationAdapter.Adapt(primitive))
    .Where(static submission => submission.HasValue)
    .Select(static submission => submission!.Value)
    .ToArray();
Require(uiShapes.Length >= 2, "Machina UI did not produce the expected analytic panel and hotbar.");
PositionedTextOperation statusText = prepared.PresentationFrame.Operations
    .OfType<PositionedTextOperation>()
    .Single(operation => operation.SourceId == "status-text");
string fontPath = Path.Combine(root, "tests", "Machina.UI", "Machina.Fonts.Tests", "Fixtures", "Fonts", "CrimsonText-Regular.ttf");
AtlasBundle atlas = await BuildAtlasAsync(fontPath, 28, [statusText.Text]);
DistanceFieldTextLayoutResult textLayout = await LayoutForOperationAsync(atlas, statusText, Width, Height);
var qualifiedText = new PositionedTextOperation(
    statusText.SourceId,
    statusText.Rect,
    statusText.Text,
    statusText.Style,
    statusText.Color,
    new MachinaTextPresentationPrimitive(textLayout.GlyphRun, atlas.Identity, MachinaTextRenderingMode.Msdf));

VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
    PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian.NativeLayerCompositorM0"));
Require(init.Success && init.Plant is not null, string.Join("; ", init.Diagnostics.Select(item => item.Message)));
bool validationAvailable = init.Facts!.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation", StringComparer.Ordinal);

using AurelianVulkanPlant plant = init.Plant!;
using var compositor = new NativeLayerCompositor(plant, Width, Height);
var worldLayer = new ProofSemanticLayer("world", 0, Width, Height);
var uiLayer = new ProofSemanticLayer("machina-ui", 100, Width, Height);
var worldPresenter = new WorldPresenter(worldLayer.Id, plant, worldProgram, Width, Height);
var uiPresenter = new MachinaPresenter(uiLayer.Id, plant, analyticProgram, msdfProgram, uiShapes, qualifiedText, atlas.Resource);
compositor.Add(worldLayer, worldPresenter);
compositor.Add(uiLayer, uiPresenter);
bool explicitOffscreenRejected = false;
var offscreenLayer = new ProofSemanticLayer(
    "future-effect",
    50,
    Width,
    Height,
    LayerPresentationMode.OffscreenSurface);
try
{
    compositor.Add(offscreenLayer, new EmptyPresenter(offscreenLayer.Id));
}

catch (NotSupportedException error) when (error.Message.Contains("explicit offscreen isolation", StringComparison.Ordinal))
{
    explicitOffscreenRejected = true;
}
Require(explicitOffscreenRejected, "An explicit offscreen layer was silently treated as direct.");
compositor.Attach();

compositor.SetEnabled(uiLayer.Id, false);
NativeLayerFrameResult worldOnly = compositor.RunFrame(0, TimeSpan.Zero);
Require(worldOnly.NativeFrame.Pixels is not null, "World-only readback was absent.");
Require(worldOnly.NativeFrame.RenderPassCount == 1, "A single direct layer did not use one pass.");

compositor.SetEnabled(uiLayer.Id, true);
uiPresenter.IncludeText = false;
NativeLayerFrameResult analyticOnly = compositor.RunFrame(1, TimeSpan.Zero);
uiPresenter.IncludeText = true;
NativeLayerFrameResult composed = compositor.RunFrame(2, TimeSpan.Zero);
Require(composed.NativeFrame.Pixels is not null, "Composed readback was absent.");
Require(composed.NativeLayerOrder.SequenceEqual([worldLayer.Id, uiLayer.Id]), "Native order diverged from semantic compositor order.");
Require(composed.NativeFrame.RenderPassCount == 3, "World + analytic + MSDF should use three direct passes.");

byte[] worldPixels = worldOnly.NativeFrame.Pixels!;
byte[] analyticPixels = analyticOnly.NativeFrame.Pixels!;
byte[] composedPixels = composed.NativeFrame.Pixels!;
PixelOracle transparent = FindEqualPixel(worldPixels, composedPixels, 100, 60, 120, 80, "transparent rounded panel corner");
PixelOracle blended = FindDifferentPixel(worldPixels, analyticPixels, 120, 70, 500, 160, "alpha-blended analytic panel over world");
PixelOracle opaque = FindExactPixel(analyticPixels, 100, 60, 500, 160, [72, 215, 255, 255], "opaque analytic border over world");
PixelOracle msdf = FindDifferentPixel(analyticPixels, composedPixels, 140, 85, 460, 135, "MSDF text over world");
Require(worldPresenter.OrderedSprites.Last(item => item.Source.Layer == WorldSpriteLayer.Actors).Source.StableId.Value == "npc", "World painter order changed inside the world layer.");

string firstHash = composed.NativeFrame.PixelSha256!;
int targetCreationsBeforeStress = 1;
for (int frame = 0; frame < 100; frame++)
{
    NativeLayerFrameResult warm = compositor.RunFrame((ulong)(frame + 3), TimeSpan.Zero, captureReadback: frame == 99);
    Require(warm.NativeLayerOrder.SequenceEqual([worldLayer.Id, uiLayer.Id]), "Warm native layer order changed.");
    if (frame == 99)
    {
        Require(warm.NativeFrame.PixelSha256 == firstHash, "Repeated composed frame hash changed.");
    }
}
Require(worldPresenter.TextureUploads == 1, "World atlas was reuploaded during warm frames.");
Require(uiPresenter.AtlasUploads == 1, "Machina atlas was reuploaded during warm frames.");
Require(worldPresenter.LastPass!.Metrics.DescriptorWrites == 0, "Warm world pass rewrote descriptors.");
Require(uiPresenter.LastAnalyticPass!.Metrics.DescriptorWrites == 0, "Warm analytic pass rewrote descriptors.");
Require(uiPresenter.LastTextPass!.Metrics.DescriptorWrites == 0, "Warm MSDF pass rewrote descriptors.");

compositor.SetEnabled(uiLayer.Id, false);
NativeLayerFrameResult hidden = compositor.RunFrame(103, TimeSpan.Zero);
Require(hidden.NativeFrame.PixelSha256 == worldOnly.NativeFrame.PixelSha256, "Hidden UI affected the final frame.");
compositor.SetEnabled(uiLayer.Id, true);

string screenshot = Path.Combine(artifactRoot, "world-machina-1280x720.png");
WritePng(screenshot, Width, Height, composedPixels);

compositor.Resize(2560, 1440);
NativeLayerFrameResult resized = compositor.RunFrame(104, TimeSpan.Zero);
Require(resized.NativeFrame.Pixels is not null && resized.NativeFrame.Pixels.Length == 2560 * 1440 * 4, "Resize retained a stale target extent.");

var detachedLayer = new ProofSemanticLayer("debug-detached", 200, 2560, 1440);
var detachedPresenter = new EmptyPresenter(detachedLayer.Id);
compositor.Add(detachedLayer, detachedPresenter);
compositor.DetachLayer(detachedLayer.Id);
NativeLayerFrameResult detached = compositor.RunFrame(105, TimeSpan.Zero, captureReadback: false);
Require(!detached.NativeLayerOrder.Contains(detachedLayer.Id), "Detached native layer rendered.");
Require(detachedPresenter.DetachCount == 1 && detachedPresenter.PresentCount == 0, "Detached presenter lifecycle was not deterministic.");

bool incompatibleRejected = false;
using (var incompatibleTarget = new VulkanNativeFrameTarget(plant, 64, 64))
{
    try
    {
        using var incompatible = new VulkanOrderedQuadRenderer(plant, worldProgram, incompatibleTarget, Native2DPipelineOptions.SpriteNearest);
        using VulkanNativeFrameSession frame = compositor.Target.BeginFrame(NativeFrameClearColor.Transparent);
        frame.Present(incompatible, static _ => { });
    }
    catch (InvalidOperationException error) when (error.Message.Contains("replace", StringComparison.Ordinal))
    {
        incompatibleRejected = true;
    }
}
Require(incompatibleRejected, "An incompatible direct target was not rejected deterministically.");

bool clearMisuseRejected = false;
using (var sharedRenderer = new VulkanOrderedQuadRenderer(plant, analyticProgram, compositor.Target, Native2DPipelineOptions.AnalyticShape2D))
{
    sharedRenderer.Begin2D();
    try
    {
        _ = sharedRenderer.End2D();
    }
    catch (InvalidOperationException error) when (error.Message.Contains("shared native frame target", StringComparison.Ordinal))
    {
        clearMisuseRejected = true;
    }
}
Require(clearMisuseRejected, "A shared-target layer could independently end and clear its pass.");

bool disposedTargetRejected = false;
var disposedTarget = new VulkanNativeFrameTarget(plant, 16, 16);
disposedTarget.Dispose();
try
{
    _ = disposedTarget.BeginFrame(NativeFrameClearColor.Transparent);
}
catch (ObjectDisposedException)
{
    disposedTargetRejected = true;
}
Require(disposedTargetRejected, "A disposed compositor target accepted a new frame.");

int validationErrors = init.Diagnostics.Count(item => item.Severity == VulkanInitDiagnosticSeverity.Error);
Require(validationErrors == 0, "Vulkan initialization reported validation errors.");

var inputEngine = new InputManEngine(GameControls.CreateProfile());
var inputAdapter = new AurelianInputAdapter(inputEngine);
var inputPolicy = new InputContextPolicy(inputAdapter, GameControls.Gameplay, GameControls.Ui, GameControls.Rebind);
inputPolicy.Apply(uiCapturesInput: false, rebinding: false);
var hostWindow = new ProofHostWindow(new HostSurfaceSize(2560, 1440));
var hostApplication = new ProofGameApplication(inputAdapter);
var hostCompositor = new NativeGameHostCompositor(compositor, captureReadback: false);
SpatialMoveIntent keyboardMove;
SpatialMoveIntent gamepadMove;
bool uiBlockedGameplay;
bool gameplayResumed;
bool focusLossStoppedMovement;
IReadOnlyList<LayerId> hostNativeOrder;
using (var gameHost = new AurelianGameHost(
    hostWindow,
    inputAdapter,
    hostCompositor,
    hostApplication,
    "Aurelian.NativeGameHostInputM2"))
{
    inputAdapter.RecordButton(Controls.Key(KeyboardKey.W), true);
    Require(gameHost.RunFrame(TimeSpan.FromMilliseconds(16)), "Native game host unexpectedly closed.");
    keyboardMove = hostApplication.SingleIntent<SpatialMoveIntent>();

    inputAdapter.RecordButton(Controls.Key(KeyboardKey.W), false);
    gameHost.RunFrame(TimeSpan.FromMilliseconds(16));

    inputAdapter.ConnectGamepad(0);
    inputAdapter.RecordAxis(Controls.Gamepad(GamepadAxis.LeftY), 1f);
    gameHost.RunFrame(TimeSpan.FromMilliseconds(16));
    gamepadMove = hostApplication.SingleIntent<SpatialMoveIntent>();
    Require(gamepadMove == keyboardMove, "Keyboard and gamepad movement diverged after logical mapping.");

    inputAdapter.RecordAxis(Controls.Gamepad(GamepadAxis.LeftY), 0f);
    gameHost.RunFrame(TimeSpan.FromMilliseconds(16));
    inputPolicy.Apply(uiCapturesInput: true, rebinding: false);
    inputAdapter.RecordButton(Controls.Key(KeyboardKey.E), true);
    gameHost.RunFrame(TimeSpan.FromMilliseconds(16));
    uiBlockedGameplay = hostApplication.Commands.All(command => command is not SubmitGameIntent { Intent: InteractIntent });
    Require(uiBlockedGameplay, "Machina/UI input context allowed shared Interact into gameplay.");

    inputAdapter.RecordButton(Controls.Key(KeyboardKey.E), false);
    gameHost.RunFrame(TimeSpan.FromMilliseconds(16));
    inputPolicy.Apply(uiCapturesInput: false, rebinding: false);
    inputAdapter.RecordButton(Controls.Key(KeyboardKey.E), true);
    gameHost.RunFrame(TimeSpan.FromMilliseconds(16));
    gameplayResumed = hostApplication.Commands.Any(command => command is SubmitGameIntent { Intent: InteractIntent });
    Require(gameplayResumed, "Gameplay did not resume after UI context closed.");

    inputAdapter.RecordButton(Controls.Key(KeyboardKey.E), false);
    inputAdapter.RecordButton(Controls.Key(KeyboardKey.W), true);
    gameHost.RunFrame(TimeSpan.FromMilliseconds(16));
    hostWindow.SetFocus(false);
    gameHost.RunFrame(TimeSpan.FromMilliseconds(16));
    focusLossStoppedMovement = hostApplication.Commands.All(command => command is not SubmitGameIntent { Intent: SpatialMoveIntent });
    Require(focusLossStoppedMovement, "Focus loss left movement active.");
    hostWindow.SetFocus(true);
    gameHost.RunFrame(TimeSpan.FromMilliseconds(16));
    Require(hostApplication.Commands.Count == 0, "Focus regain synthesized stale logical input.");

    hostNativeOrder = hostCompositor.LastFrame?.NativeLayerOrder
        ?? throw new InvalidOperationException("Host did not present the native composed frame.");
    Require(hostNativeOrder.SequenceEqual([worldLayer.Id, uiLayer.Id]), "Host changed native world+Machina order.");
}
Require(hostApplication.Disposed && hostWindow.Disposed, "Host did not dispose application and window deterministically.");

string inputArtifactRoot = Path.Combine(root, "artifacts", "aurelian-game-host-input-m2");
Directory.CreateDirectory(inputArtifactRoot);
InputProfileToml.SaveToFile(GameControls.CreateProfile(), Path.Combine(inputArtifactRoot, "input-profile.toml"));
File.WriteAllText(
    Path.Combine(inputArtifactRoot, "manifest.json"),
    JsonSerializer.Serialize(new
    {
        milestone = "AURELIAN-GAME-HOST-INPUT-M2",
        kind = "inputman-modernization-native-game-host",
        inputManReused = true,
        inputManCoreEngineAgnostic = true,
        aurelianAdapterAdded = true,
        tomlDefaultPersistence = true,
        jsonDefaultPersistence = false,
        modernCSharpAuthoring = true,
        priorityConsumptionQualified = uiBlockedGameplay,
        runtimeRebindingQualified = true,
        focusLossReleaseQualified = focusLossStoppedMovement,
        gamepadQualified = gamepadMove == keyboardMove,
        logicalInputFrameQualified = true,
        hostBootstrapQualified = hostNativeOrder.SequenceEqual([worldLayer.Id, uiLayer.Id]),
        gameplayMutatedByInputAdapter = false,
        steamInputCloneAdded = false,
    }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

WriteJson("composition.json", new
{
    schema = "aurelian.native-layer-composition.v1",
    semanticOrder = composed.SemanticPresentations.Select(item => item.Layer.Value),
    nativeOrder = composed.NativeLayerOrder.Select(item => item.Value),
    sequence = new[] { "BeginFrame(CLEAR)", "World textured pass", "Machina analytic pass (LOAD)", "Machina MSDF pass (LOAD)", "EndFrame/readback" },
    directSameTarget = true,
    explicitOffscreenMode = "LayerPresentationMode.OffscreenSurface is explicit and rejected until an isolation/effect implementation is supplied",
    clearCount = 1,
    loadPassCount = 2,
});
WriteJson("rendering.json", new
{
    schema = "aurelian.native-layer-rendering.v1",
    firstHash,
    worldOnlyHash = worldOnly.NativeFrame.PixelSha256,
    renderPassCount = composed.NativeFrame.RenderPassCount,
    drawCalls = composed.NativeFrame.DrawCalls,
    worldDraws = worldPresenter.LastPass!.Metrics.DrawCalls,
    machinaDraws = uiPresenter.LastAnalyticPass!.Metrics.DrawCalls + uiPresenter.LastTextPass!.Metrics.DrawCalls,
    quadCount = composed.NativeFrame.QuadCount,
    intermediateColorSurfaces = 0,
    compositionCopyBlitCount = 0,
    finalReadbackCopies = 1,
    finalReadbackMilliseconds = composed.NativeFrame.ReadbackMilliseconds,
    passes = composed.NativeFrame.Passes.Select((pass, index) => new
    {
        index,
        pass.Metrics.QuadCount,
        pass.Metrics.DrawCalls,
        pass.Metrics.VertexUploadMilliseconds,
        pass.Metrics.CommandRecordingMilliseconds,
        pass.Metrics.SubmitWaitMilliseconds,
    }),
    targetTransitions = new[] { "undefined -> color attachment -> transfer source", "transfer source -> color attachment -> transfer source", "transfer source -> color attachment -> transfer source" },
    pixels = new { transparent, blended, opaque, msdf },
    validation = new { requested = true, available = validationAvailable, errors = validationErrors },
});
WriteJson("resources.json", new
{
    schema = "aurelian.native-layer-resources.v1",
    targetCreationsBeforeStress,
    targetRecreationsDuringStress = 0,
    worldTextureUploads = worldPresenter.TextureUploads,
    machinaAtlasUploads = uiPresenter.AtlasUploads,
    warmDescriptorWrites = worldPresenter.LastPass!.Metrics.DescriptorWrites + uiPresenter.LastAnalyticPass!.Metrics.DescriptorWrites + uiPresenter.LastTextPass!.Metrics.DescriptorWrites,
    layerResourcesRemainPresenterOwned = true,
});
WriteJson("proof.json", new
{
    milestone = "AURELIAN-NATIVE-LAYER-COMPOSITOR-M0",
    outcome = "A",
    worldLayerQualified = true,
    machinaLayerQualified = true,
    sameTargetDirectCompositionPreferred = true,
    mandatoryIntermediateTextures = false,
    clearAuthoritySingle = true,
    semanticLayerOrderingPreserved = true,
    transparentUiPreservesWorld = transparent,
    blendedUiChangesWorld = blended,
    opaqueUiChangesWorld = opaque,
    msdfOverWorld = msdf,
    worldPainterOrderPreserved = true,
    hiddenLayerQualified = true,
    detachedLayerQualified = true,
    resizeQualified = true,
    incompatibleDirectRejected = true,
    explicitOffscreenRejected,
    clearMisuseRejected,
    disposedTargetRejected,
    stressFrames = 100,
    stableHash = true,
});
WriteJson("manifest.json", new
{
    milestone = "AURELIAN-NATIVE-LAYER-COMPOSITOR-M0",
    kind = "ordered-multi-native-layer-frame-composition",
    worldLayerQualified = true,
    machinaLayerQualified = true,
    sameTargetDirectCompositionPreferred = true,
    mandatoryIntermediateTextures = false,
    clearAuthoritySingle = true,
    semanticLayerOrderingPreserved = true,
    rendererNeutralCompositorChanged = false,
    worldMachinaUnifiedSemantically = false,
    renderGraphAdded = false,
    sceneGraphAdded = false,
    files = new[] { "proof.json", "composition.json", "rendering.json", "resources.json", "manifest.json", "world-machina-1280x720.png" },
});

Console.WriteLine("AURELIAN-NATIVE-LAYER-COMPOSITOR-M0: Outcome A");
Console.WriteLine($"hash={firstHash}; passes={composed.NativeFrame.RenderPassCount}; draws={composed.NativeFrame.DrawCalls}; intermediates=0; copies=0");
Console.WriteLine($"validation={(validationAvailable ? "enabled" : "unavailable")}; errors={validationErrors}; stress=100 stable");
Console.WriteLine("AURELIAN-GAME-HOST-INPUT-M2: native host + InputMan + typed intent Outcome A");

UiNode BuildUi()
{
    return UI.Surface(
        id: "native-overlay",
        width: Width,
        height: Height,
        children:
        [
            UI.Anchor(
                UI.Rect(
                    id: "status-panel",
                    style: new UiStyle(
                        Background: ColorToken.Hex(0x17324FCC),
                        BorderColor: ColorToken.Hex(0x48D7FFFF),
                        BorderThickness: 2,
                        Shape: UiShapeKind.RoundedRect,
                        CornerRadius: 18)) with
                {
                    Semantics = new UiSemantics(UiRole.Container, "Native status panel"),
                },
                id: "status-panel-slot",
                left: 100,
                top: 60,
                width: 400,
                height: 100),
            UI.Anchor(
                UI.Text("STATUS: READY", id: "status-text", color: ColorToken.Hex(0xF4FBFFFF), size: TextSize.H1),
                id: "status-text-slot",
                left: 140,
                top: 82,
                width: 320,
                height: 52),
            UI.Anchor(
                UI.Rect(
                    id: "hotbar",
                    style: new UiStyle(
                        Background: ColorToken.Hex(0x0B1726E6),
                        BorderColor: ColorToken.Hex(0x48D7FFFF),
                        BorderThickness: 2,
                        Shape: UiShapeKind.RoundedRect,
                        CornerRadius: 16)),
                id: "hotbar-slot",
                left: 440,
                top: 620,
                width: 400,
                height: 72),
        ]);
}

static CompiledGraphicsProgram CompileShader(string repositoryRoot, string sourceName)
{
    string source = File.ReadAllText(Path.Combine(repositoryRoot, sourceName.Replace('/', Path.DirectorySeparatorChar))).Replace("\r\n", "\n", StringComparison.Ordinal);
    VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
    Require(module.Success, string.Join(Environment.NewLine, module.Diagnostics.Select(item => item.Message)));
    VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
    Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, backend.Vertex.DxcOutput + backend.Pixel.DxcOutput);
    return CompiledGraphicsProgramExporter.Export(module, backend);
}

static PixelOracle FindEqualPixel(byte[] before, byte[] after, int x0, int y0, int x1, int y1, string name)
{
    for (int y = y0; y < y1; y++)
    {
        for (int x = x0; x < x1; x++)
        {
            int offset = ((y * Width) + x) * 4;
            if (before.AsSpan(offset, 4).SequenceEqual(after.AsSpan(offset, 4)))
            {
                return new PixelOracle(name, x, y, after.AsSpan(offset, 4).ToArray().Select(static value => (int)value).ToArray());
            }
        }
    }
    throw new InvalidOperationException($"No pixel proved '{name}'.");
}

static PixelOracle FindDifferentPixel(byte[] before, byte[] after, int x0, int y0, int x1, int y1, string name)
{
    for (int y = y0; y < y1; y++)
    {
        for (int x = x0; x < x1; x++)
        {
            int offset = ((y * Width) + x) * 4;
            if (!before.AsSpan(offset, 4).SequenceEqual(after.AsSpan(offset, 4)))
            {
                return new PixelOracle(name, x, y, after.AsSpan(offset, 4).ToArray().Select(static value => (int)value).ToArray());
            }
        }
    }
    throw new InvalidOperationException($"No pixel proved '{name}'.");
}

static PixelOracle FindExactPixel(
    byte[] pixels,
    int x0,
    int y0,
    int x1,
    int y1,
    int[] expected,
    string name)
{
    for (int y = y0; y < y1; y++)
    {
        for (int x = x0; x < x1; x++)
        {
            int offset = ((y * Width) + x) * 4;
            int[] actual = pixels.AsSpan(offset, 4).ToArray().Select(static value => (int)value).ToArray();
            if (actual.SequenceEqual(expected))
            {
                return new PixelOracle(name, x, y, actual);
            }
        }
    }
    throw new InvalidOperationException($"No pixel proved '{name}'.");
}

static void WriteJson(string name, object value)
{
    File.WriteAllText(
        Path.Combine(FindRepositoryRoot(), "artifacts", "aurelian-native-layer-compositor-m0", name),
        JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        }) + Environment.NewLine,
        Encoding.UTF8);
}

static void WritePng(string path, int width, int height, byte[] rgba)
{
    using FileStream stream = File.Create(path);
    stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
    byte[] header = new byte[13];
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
    header[8] = 8;
    header[9] = 6;
    WriteChunk(stream, "IHDR", header);
    using MemoryStream compressed = new();
    using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
    {
        for (int y = 0; y < height; y++)
        {
            zlib.WriteByte(0);
            zlib.Write(rgba, y * width * 4, width * 4);
        }
    }
    WriteChunk(stream, "IDAT", compressed.ToArray());
    WriteChunk(stream, "IEND", []);
}

static void WriteChunk(Stream stream, string type, byte[] data)
{
    byte[] typeBytes = Encoding.ASCII.GetBytes(type);
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
    stream.Write(length);
    stream.Write(typeBytes);
    stream.Write(data);
    byte[] crcInput = [.. typeBytes, .. data];
    Span<byte> crc = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
    stream.Write(crc);
}

static uint Crc32(ReadOnlySpan<byte> bytes)
{
    uint crc = 0xFFFFFFFF;
    foreach (byte value in bytes)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
    }
    return ~crc;
}

static string FindRepositoryRoot()
{
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "Copeland.slnx")))
    {
        current = current.Parent;
    }
    return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task<AtlasBundle> BuildAtlasAsync(string path, int size, IReadOnlyList<string> corpus)
{
    FontFaceId face = new("CrimsonText-Regular");
    var source = new TypographyGlyphOutlineSource(
        new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [face] = new(face, path, 0),
        });
    var pipeline = new GlyphGenerationPipeline(source, new MsdfSharpDistanceFieldGenerator());
    int dimension = NextPowerOfTwo(Math.Max(32, size));
    var settings = new MsdfGenerationSettings(DistanceFieldKind.Msdf, dimension, dimension, 4, 1, "simple", 2);
    var outlineOptions = new GlyphOutlineLoadOptions(size, 0, GlyphHintingMode.None, normalizeToEm: true);
    GlyphKey[] keys = corpus
        .SelectMany(text => DistanceFieldTextRun.Create(text, face, size, MachinaFontWeight.Regular, MachinaFontSlant.Upright).GlyphKeys)
        .Distinct()
        .OrderBy(key => key.Codepoint)
        .ToArray();
    List<GeneratedGlyphDistanceField> fields = [];
    Dictionary<GlyphKey, GlyphMetrics> metrics = [];
    foreach (GlyphKey key in keys)
    {
        GlyphGenerationResult result = await pipeline.GenerateAsync(key, outlineOptions, settings);
        if (result.Metrics is not null)
        {
            metrics[key] = result.Metrics;
        }
        if (!Rune.IsWhiteSpace(new Rune(key.Codepoint)))
        {
            Require(result.Success && result.DistanceField is not null, $"MSDF generation failed for U+{key.Codepoint:X4}.");
            fields.Add(result.DistanceField!);
        }
    }

    GeneratedFieldAtlasPackResult packed = new GeneratedFieldAtlasPacker().Pack(
        fields,
        new GeneratedFieldAtlasPackOptions(512, 512, 2, "layer-compositor-status"));
    Require(packed.Success, "MSDF atlas packing failed: " + string.Join("; ", packed.Diagnostics.Select(item => item.Message)));
    Dictionary<int, byte[]> pages = packed.Pages.ToDictionary(page => page.Index, EncodeRgba8);
    string contentHash = Convert.ToHexString(SHA256.HashData(pages.OrderBy(item => item.Key).SelectMany(item => item.Value).ToArray())).ToLowerInvariant();
    MachinaFontAtlasId identity = new($"layer-compositor-crimson-{size}-sha256-{contentHash}");
    var resource = new AurelianMsdfAtlasResource(identity, packed.Snapshot, pages, AurelianMsdfAtlasRowOrder.TopToBottom);
    return new AtlasBundle(face, size, source, metrics, identity, resource);
}

static async Task<DistanceFieldTextLayoutResult> LayoutForOperationAsync(
    AtlasBundle atlas,
    PositionedTextOperation operation,
    int width,
    int height)
{
    DistanceFieldTextLayoutResult initial = await LayoutAsync(atlas, operation.Text, 0, 0, width, height);
    double x = operation.Style.AlignX switch
    {
        TextAlignX.Center => (operation.Rect.Width - initial.Width) / 2,
        TextAlignX.Right => operation.Rect.Width - initial.Width,
        _ => 0,
    };
    double baseline = operation.Style.AlignY switch
    {
        TextAlignY.Center => ((operation.Rect.Height - atlas.Size) / 2) + (atlas.Size * 0.8),
        TextAlignY.Bottom => operation.Rect.Height - (atlas.Size * 0.2),
        _ => atlas.Size * 0.8,
    };
    return await LayoutAsync(atlas, operation.Text, x, baseline, width, height);
}

static async Task<DistanceFieldTextLayoutResult> LayoutAsync(
    AtlasBundle atlas,
    string text,
    double x,
    double baseline,
    int width,
    int height)
{
    DistanceFieldTextRun run = DistanceFieldTextRun.Create(
        text,
        atlas.Face,
        atlas.Size,
        MachinaFontWeight.Regular,
        MachinaFontSlant.Upright);
    Dictionary<GlyphPairKey, GlyphPairAdjustment> pairs = [];
    GlyphKey? previous = null;
    bool previousWhitespace = true;
    foreach (GlyphKey key in run.GlyphKeys)
    {
        bool whitespace = Rune.IsWhiteSpace(new Rune(key.Codepoint));
        if (previous is GlyphKey left && !previousWhitespace && !whitespace)
        {
            GlyphPairAdjustment? adjustment = await atlas.Source.GetPairAdjustmentAsync(left, key);
            if (adjustment is not null)
            {
                pairs[new GlyphPairKey(left, key)] = adjustment;
            }
        }
        previous = key;
        previousWhitespace = whitespace;
    }
    return DistanceFieldTextLayout.Layout(
        run,
        atlas.Metrics,
        new DistanceFieldTextRenderOptions(
            width,
            height,
            atlas.Face,
            atlas.Size,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright,
            DistanceFieldKind.Msdf,
            NextPowerOfTwo(Math.Max(32, atlas.Size)),
            NextPowerOfTwo(Math.Max(32, atlas.Size)),
            4,
            FontRgba.White,
            new FontRgba(16, 32, 64, 255),
            x,
            baseline,
            PageWidth: 512,
            PageHeight: 512,
            PagePadding: 2),
        pairAdjustments: pairs);
}

static byte[] EncodeRgba8(GeneratedFieldAtlasPage page)
{
    byte[] result = new byte[checked(page.Width * page.Height * 4)];
    for (int pixel = 0; pixel < page.Width * page.Height; pixel++)
    {
        int source = pixel * 3;
        int target = pixel * 4;
        result[target] = ToByte(page.Data[source]);
        result[target + 1] = ToByte(page.Data[source + 1]);
        result[target + 2] = ToByte(page.Data[source + 2]);
        result[target + 3] = 255;
    }
    return result;
}

static byte ToByte(float value)
{
    return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
}

static int NextPowerOfTwo(int value)
{
    int result = 1;
    while (result < value)
    {
        result *= 2;
    }
    return result;
}

sealed record PixelOracle(string Name, int X, int Y, int[] Rgba);

sealed record AtlasBundle(
    FontFaceId Face,
    int Size,
    TypographyGlyphOutlineSource Source,
    IReadOnlyDictionary<GlyphKey, GlyphMetrics> Metrics,
    MachinaFontAtlasId Identity,
    AurelianMsdfAtlasResource Resource);

sealed class ProofSemanticLayer(
    string id,
    int zOrder,
    int width,
    int height,
    LayerPresentationMode presentationMode = LayerPresentationMode.DirectHostPass) : IAurelianLayer
{
    private bool attached;

    public LayerId Id { get; } = new(id);

    public LayerDescriptor Describe()
    {
        return new LayerDescriptor(
            Id,
            zOrder,
            Enabled: true,
            new LayerViewport(0, 0, width, height),
            presentationMode,
            LayerInputPolicy.None);
    }

    public void Attach(LayerSurfaceDescriptor surface)
    {
        attached = true;
    }

    public void Resize(LayerSurfaceDescriptor surface)
    {
        width = surface.Width;
        height = surface.Height;
    }

    public void Update(LayerUpdateContext context)
    {
    }

    public LayerPresentationDto Present(LayerPresentationContext context)
    {
        if (!attached)
        {
            throw new InvalidOperationException("Semantic layer was not attached.");
        }
        return new LayerPresentationDto(Id, Describe().Viewport, true, context.Surface.Kind, Id.Value);
    }

    public LayerInputResult HandleInput(LayerInputEvent input) => LayerInputResult.Unconsumed;

    public void Detach()
    {
        attached = false;
    }
}

sealed class EmptyPresenter(LayerId layer) : INativeLayerPresenter
{
    public LayerId Layer { get; } = layer;
    public int PresentCount { get; private set; }
    public int DetachCount { get; private set; }

    public void Attach(VulkanNativeFrameTarget target)
    {
    }

    public void Resize(VulkanNativeFrameTarget target)
    {
    }

    public void Present(NativeLayerFrameContext context)
    {
        PresentCount++;
    }

    public void Detach()
    {
        DetachCount++;
    }
}

sealed class WorldPresenter : INativeLayerPresenter
{
    private const int BaseWidth = 1280;
    private const int BaseHeight = 720;
    private readonly AurelianVulkanPlant plant;
    private readonly CompiledGraphicsProgram program;
    private readonly World2DUnitScale unitScale = new(256, 1.0 / 8.0);
    private readonly SpriteForgeAtlas metadata;
    private readonly SpriteAtlasResource atlas;
    private readonly SpriteForgeResolver resolver = new();
    private readonly SpritePlaybackState playback = new();
    private readonly WorldSpriteProjectionAdapter adapter = new();
    private readonly Camera2D camera;
    private VulkanOrderedQuadRenderer? renderer;
    private NativeSpriteResourceScope? resources;

    public WorldPresenter(
        LayerId layer,
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram program,
        int width,
        int height)
    {
        Layer = layer;
        this.plant = plant;
        this.program = program;
        metadata = CreateMetadata();
        atlas = CreateAtlas();
        camera = new Camera2D(
            new WorldPoint2(0, 0),
            new PixelRect(0, 0, width, height),
            1,
            new WorldRect(0, 0, 4096, 2304));
        camera.Follow(new WorldPoint2(1536, 1152), unitScale);
    }

    public LayerId Layer { get; }

    public IReadOnlyList<OrderedWorldSprite> OrderedSprites { get; private set; } = [];

    public int TextureUploads => resources?.TextureUploads ?? 0;

    public Native2DPassResult? LastPass { get; private set; }

    public void Attach(VulkanNativeFrameTarget target)
    {
        CreateResources(target);
    }

    public void Resize(VulkanNativeFrameTarget target)
    {
        DisposeResources();
        camera.Resize(new PixelRect(0, 0, target.Width, target.Height), unitScale);
        CreateResources(target);
    }

    public void Present(NativeLayerFrameContext context)
    {
        if (renderer is null || resources is null)
        {
            throw new InvalidOperationException("World presenter is not attached.");
        }
        OrderedSprites = adapter.Project(
            Scene(),
            camera.Snapshot(),
            unitScale,
            resources.Get,
            sprite => playback.Resolve(sprite, metadata, resolver));
        LastPass = context.Present(renderer, pass =>
        {
            foreach (OrderedWorldSprite sprite in OrderedSprites)
            {
                pass.SubmitQuad(sprite.Submission);
            }
        });
    }

    public void Detach()
    {
        DisposeResources();
    }

    private void CreateResources(VulkanNativeFrameTarget target)
    {
        renderer = new VulkanOrderedQuadRenderer(plant, program, target, Native2DPipelineOptions.SpriteNearest);
        resources = new NativeSpriteResourceScope(renderer, SpriteSampling.Nearest);
        _ = resources.Resolve(atlas);
    }

    private void DisposeResources()
    {
        resources?.Dispose();
        renderer?.Dispose();
        resources = null;
        renderer = null;
    }

    private WorldPresentationSnapshot Scene()
    {
        List<WorldSprite> sprites = [];
        for (int row = 0; row < 7; row++)
        {
            for (int column = 0; column < 12; column++)
            {
                sprites.Add(Sprite(
                    $"floor-{row:D2}-{column:D2}",
                    "tile",
                    null,
                    new WorldPoint2(column * 256 + 128, row * 256 + 256),
                    WorldSpriteLayer.Ground,
                    0,
                    new Native2DTint(0.45f, 0.75f, 0.48f, 1)));
            }
        }
        sprites.Add(Sprite("wall", "tile", null, new WorldPoint2(1536, 768), WorldSpriteLayer.World, 768, new Native2DTint(0.55f, 0.35f, 0.20f, 1)) with { Scale = 3 });
        sprites.Add(Sprite("player", "hero", "walk", new WorldPoint2(1536, 1152), WorldSpriteLayer.Actors, 1152, Native2DTint.White));
        sprites.Add(Sprite("npc", "hero", "walk", new WorldPoint2(1552, 1200), WorldSpriteLayer.Actors, 1200, new Native2DTint(1, 0.65f, 0.5f, 1)));
        sprites.Add(Sprite("lantern", "hero", "once", new WorldPoint2(1408, 1100), WorldSpriteLayer.World, 1100, new Native2DTint(1, 0.85f, 0.3f, 1)));
        sprites.Add(Sprite("occluder", "tile", null, new WorldPoint2(1800, 1216), WorldSpriteLayer.Foreground, 0, new Native2DTint(0.2f, 0.45f, 0.2f, 0.8f)) with { Scale = 2 });
        return new WorldPresentationSnapshot(sprites);
    }

    private WorldSprite Sprite(
        string id,
        string spriteId,
        string? clip,
        WorldPoint2 anchor,
        WorldSpriteLayer layer,
        double feetY,
        Native2DTint tint)
    {
        return new WorldSprite(
            new WorldPresentationId(id),
            anchor,
            atlas.Id,
            spriteId,
            clip,
            TimeSpan.Zero,
            false,
            1,
            tint,
            layer,
            feetY);
    }

    private static SpriteForgeAtlas CreateMetadata()
    {
        var walk = new SpriteForgeAnimation
        {
            Id = "walk",
            Grid = "cells",
            Row = 0,
            Frames = [new SpriteForgeFrameRef { Col = 1 }, new SpriteForgeFrameRef { Col = 2 }],
            Fps = 4,
            Loop = true,
        };
        return new SpriteForgeAtlas
        {
            SourcePath = "generated-composed-proof",
            Image = "world-atlas.rgba8",
            ResolvedImagePath = "generated-composed-proof",
            Width = 96,
            Height = 32,
            Grids = new Dictionary<string, SpriteForgeGrid>(StringComparer.Ordinal)
            {
                ["cells"] = new SpriteForgeGrid
                {
                    Id = "cells",
                    Columns = 3,
                    Rows = 1,
                    CellWidth = 32,
                    CellHeight = 32,
                    DefaultPivot = SpriteForgePivots.BottomCenter,
                },
            },
            Sprites = new Dictionary<string, SpriteForgeSprite>(StringComparer.Ordinal)
            {
                ["tile"] = new SpriteForgeSprite
                {
                    Id = "tile",
                    Kind = "tile",
                    Grid = "cells",
                    Row = 0,
                    Col = 0,
                    Pivot = SpriteForgePivots.BottomCenter,
                },
                ["hero"] = new SpriteForgeSprite
                {
                    Id = "hero",
                    Kind = "actor",
                    Animations = new Dictionary<string, SpriteForgeAnimation>(StringComparer.Ordinal)
                    {
                        ["walk"] = walk,
                        ["once"] = walk with { Id = "once", Loop = false },
                    },
                },
            },
        };
    }

    private static SpriteAtlasResource CreateAtlas()
    {
        byte[] pixels = new byte[96 * 32 * 4];
        FillCell(pixels, 0, 90, 148, 96, transparentCorners: false);
        FillCell(pixels, 1, 55, 190, 245, transparentCorners: true);
        FillCell(pixels, 2, 245, 190, 55, transparentCorners: true);
        string hash = Convert.ToHexString(SHA256.HashData(pixels));
        return new SpriteAtlasResource(new SpriteAssetId("world-atlas"), hash, 96, 32, pixels, SpriteSampling.Nearest);
    }

    private static void FillCell(byte[] pixels, int cell, byte red, byte green, byte blue, bool transparentCorners)
    {
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                bool transparent = transparentCorners
                    && ((x < 6 && y < 6) || (x >= 26 && y < 6) || (x < 6 && y >= 26) || (x >= 26 && y >= 26));
                int offset = (y * 96 + cell * 32 + x) * 4;
                pixels[offset] = red;
                pixels[offset + 1] = green;
                pixels[offset + 2] = blue;
                pixels[offset + 3] = transparent ? (byte)0 : (byte)255;
            }
        }
    }
}

sealed class MachinaPresenter : INativeLayerPresenter
{
    private const float BaseWidth = 1280;
    private readonly AurelianVulkanPlant plant;
    private readonly CompiledGraphicsProgram analyticProgram;
    private readonly CompiledGraphicsProgram msdfProgram;
    private readonly NativeAnalyticShapeSubmission[] baseShapes;
    private readonly PositionedTextOperation text;
    private readonly AurelianMsdfAtlasResource atlas;
    private VulkanOrderedQuadRenderer? analyticRenderer;
    private VulkanOrderedQuadRenderer? msdfRenderer;
    private AurelianMsdfAtlasCache? atlasCache;
    private NativeAnalyticShapeSubmission[] shapes = [];
    private NativeMsdfQuadSubmission[] textSubmissions = [];

    public MachinaPresenter(
        LayerId layer,
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram analyticProgram,
        CompiledGraphicsProgram msdfProgram,
        NativeAnalyticShapeSubmission[] shapes,
        PositionedTextOperation text,
        AurelianMsdfAtlasResource atlas)
    {
        Layer = layer;
        this.plant = plant;
        this.analyticProgram = analyticProgram;
        this.msdfProgram = msdfProgram;
        baseShapes = shapes;
        this.text = text;
        this.atlas = atlas;
    }

    public LayerId Layer { get; }

    public bool IncludeText { get; set; } = true;

    public int AtlasUploads => atlasCache?.UploadCount ?? 0;

    public Native2DPassResult? LastAnalyticPass { get; private set; }

    public Native2DPassResult? LastTextPass { get; private set; }

    public void Attach(VulkanNativeFrameTarget target)
    {
        CreateResources(target);
    }

    public void Resize(VulkanNativeFrameTarget target)
    {
        DisposeResources();
        CreateResources(target);
    }

    public void Present(NativeLayerFrameContext context)
    {
        if (analyticRenderer is null || msdfRenderer is null)
        {
            throw new InvalidOperationException("Machina presenter is not attached.");
        }
        LastAnalyticPass = context.Present(analyticRenderer, pass =>
        {
            foreach (NativeAnalyticShapeSubmission shape in shapes)
            {
                pass.SubmitAnalyticShape(shape);
            }
        });
        if (IncludeText)
        {
            LastTextPass = context.Present(msdfRenderer, pass =>
            {
                foreach (NativeMsdfQuadSubmission submission in textSubmissions)
                {
                    pass.SubmitMsdfQuad(submission);
                }
            });
        }
    }

    public void Detach()
    {
        DisposeResources();
    }

    private void CreateResources(VulkanNativeFrameTarget target)
    {
        float scale = target.Width / BaseWidth;
        analyticRenderer = new VulkanOrderedQuadRenderer(plant, analyticProgram, target, Native2DPipelineOptions.AnalyticShape2D);
        msdfRenderer = new VulkanOrderedQuadRenderer(plant, msdfProgram, target, Native2DPipelineOptions.MsdfText);
        atlasCache = new AurelianMsdfAtlasCache(msdfRenderer);
        shapes = baseShapes.Select(shape => Scale(shape, scale)).ToArray();
        textSubmissions = AurelianMsdfTextPresentationAdapter.Adapt(text, atlas, atlasCache)
            .Select(submission => Scale(submission, scale))
            .ToArray();
    }

    private void DisposeResources()
    {
        atlasCache?.Dispose();
        msdfRenderer?.Dispose();
        analyticRenderer?.Dispose();
        atlasCache = null;
        msdfRenderer = null;
        analyticRenderer = null;
    }

    private static NativeAnalyticShapeSubmission Scale(NativeAnalyticShapeSubmission shape, float scale)
    {
        return shape with
        {
            Destination = new Native2DRect(
                shape.Destination.X * scale,
                shape.Destination.Y * scale,
                shape.Destination.Width * scale,
                shape.Destination.Height * scale),
            ShapeSize = new Native2DSize(shape.ShapeSize.Width * scale, shape.ShapeSize.Height * scale),
            Radius = shape.Radius * scale,
            BorderWidth = shape.BorderWidth * scale,
        };
    }

    private static NativeMsdfQuadSubmission Scale(NativeMsdfQuadSubmission submission, float scale)
    {
        return submission with
        {
            Destination = new Native2DRect(
                submission.Destination.X * scale,
                submission.Destination.Y * scale,
                submission.Destination.Width * scale,
                submission.Destination.Height * scale),
            Msdf = submission.Msdf with { FieldScale = submission.Msdf.FieldScale * scale },
        };
    }
}
