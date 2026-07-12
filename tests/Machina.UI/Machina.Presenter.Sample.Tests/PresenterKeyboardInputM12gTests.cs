using System.Reflection;
using Avalonia.Input;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PresenterKeyboardInputM12gTests
{
    [Fact]
    public void AvaloniaKeyboardInputBackend_IsSampleScoped()
    {
        Assert.Equal(
            typeof(PresenterNavigationState).Assembly,
            typeof(AvaloniaPresenterInputBackend).Assembly);
        Assert.StartsWith("Machina.Presenter.Sample", typeof(AvaloniaPresenterInputBackend).Namespace, StringComparison.Ordinal);
    }

    [Fact]
    public void PresenterKeyboardInput_DoesNotReferenceAvaloniaTypes()
    {
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterKey));
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterKeyModifiers));
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterKeyboardInput));
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterKeyboardInputRouter));
    }

    [Fact]
    public void PresenterNavigationDispatch_DoesNotReferenceAvaloniaTypes()
    {
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterNavigationDispatch));
    }

    [Fact]
    public void OblivionCardHandlers_DoNotReferenceAvaloniaTypes()
    {
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(OblivionCardHandlerRegistry));
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(OblivionCardHandlerBase));
    }

    [Fact]
    public void AvaloniaKeyboardInputBackend_MapsArrowKeys()
    {
        var backend = new AvaloniaPresenterInputBackend();

        Assert.Equal(PresenterKey.ArrowUp, backend.TranslateKeyDown(Key.Up).Keyboard!.Key);
        Assert.Equal(PresenterKey.ArrowDown, backend.TranslateKeyDown(Key.Down).Keyboard!.Key);
        Assert.Equal(PresenterKey.ArrowLeft, backend.TranslateKeyDown(Key.Left).Keyboard!.Key);
        Assert.Equal(PresenterKey.ArrowRight, backend.TranslateKeyDown(Key.Right).Keyboard!.Key);
    }

    [Fact]
    public void AvaloniaKeyboardInputBackend_MapsPageHomeEndKeys()
    {
        var backend = new AvaloniaPresenterInputBackend();

        Assert.Equal(PresenterKey.PageUp, backend.TranslateKeyDown(Key.PageUp).Keyboard!.Key);
        Assert.Equal(PresenterKey.PageDown, backend.TranslateKeyDown(Key.PageDown).Keyboard!.Key);
        Assert.Equal(PresenterKey.Home, backend.TranslateKeyDown(Key.Home).Keyboard!.Key);
        Assert.Equal(PresenterKey.End, backend.TranslateKeyDown(Key.End).Keyboard!.Key);
    }

    [Fact]
    public void AvaloniaKeyboardInputBackend_MapsEnterEscapeTabSpace()
    {
        var backend = new AvaloniaPresenterInputBackend();

        Assert.Equal(PresenterKey.Enter, backend.TranslateKeyDown(Key.Enter).Keyboard!.Key);
        Assert.Equal(PresenterKey.Escape, backend.TranslateKeyDown(Key.Escape).Keyboard!.Key);
        Assert.Equal(PresenterKey.Tab, backend.TranslateKeyDown(Key.Tab).Keyboard!.Key);
        Assert.Equal(PresenterKey.Space, backend.TranslateKeyDown(Key.Space).Keyboard!.Key);
    }

    [Fact]
    public void AvaloniaKeyboardInputBackend_MapsModifiers()
    {
        var backend = new AvaloniaPresenterInputBackend();

        PresenterKeyboardInput keyboard = backend.TranslateKeyDown(
            Key.R,
            KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta).Keyboard!;

        Assert.True(keyboard.Modifiers.Ctrl);
        Assert.True(keyboard.Modifiers.Shift);
        Assert.True(keyboard.Modifiers.Alt);
        Assert.True(keyboard.Modifiers.Meta);
    }

    [Fact]
    public void AvaloniaKeyboardInputBackend_MapsUnknownKeyDeterministically()
    {
        var backend = new AvaloniaPresenterInputBackend();

        PresenterInputEvent input = backend.TranslateKeyDown((Key)(-1));

        Assert.Equal(PresenterInputKind.KeyDown, input.Kind);
        Assert.Equal(PresenterKey.Unknown, input.Keyboard!.Key);
    }

    [Fact]
    public void AvaloniaKeyboardInputBackend_TranslatesTextInputIfAvailable()
    {
        var backend = new AvaloniaPresenterInputBackend();

        PresenterInputEvent input = backend.TranslateTextInput("x");

        Assert.Equal(PresenterInputKind.TextInput, input.Kind);
        Assert.Equal(PresenterKey.Unknown, input.Keyboard!.Key);
        Assert.Equal("x", input.Keyboard.Text);
    }

    [Fact]
    public void KeyboardRoute_PageDownScrollsSelectedPage()
    {
        PresenterNavigationState state = ControlsPageState(0);

        PresenterNavigationState next = DispatchInput(state, KeyDown(PresenterKey.PageDown));

        Assert.True(next.GetScrollOffset("components.controls") > 0);
    }

    [Fact]
    public void KeyboardRoute_PageUpScrollsSelectedPage()
    {
        PresenterNavigationState state = ControlsPageState(200);

        PresenterNavigationState next = DispatchInput(state, KeyDown(PresenterKey.PageUp));

        Assert.True(next.GetScrollOffset("components.controls") < 200);
    }

    [Fact]
    public void KeyboardRoute_HomeScrollsToTop()
    {
        PresenterNavigationState state = ControlsPageState(200);

        PresenterNavigationState next = DispatchInput(state, KeyDown(PresenterKey.Home));

        Assert.Equal(0, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void KeyboardRoute_EndScrollsToBottom()
    {
        PresenterNavigationState state = ControlsPageState(0);

        PresenterNavigationState next = DispatchInput(state, KeyDown(PresenterKey.End));
        double expected = PresenterScrollRegion.ComputeMaxScrollOffset(
            PresenterNavigationCatalog.GetPageContentHeight("components.controls", ProofOptions),
            PresenterNavigationLayout.Default.ViewportHeight);

        Assert.Equal(expected, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void KeyboardRoute_CtrlArrowDownSelectsNextSection()
    {
        PresenterNavigationState next = DispatchInput(
            PresenterNavigationState.CreateDefault(Model),
            KeyDown(PresenterKey.ArrowDown, ctrl: true));

        Assert.Equal("components", next.SelectedSectionId);
    }

    [Fact]
    public void KeyboardRoute_CtrlArrowUpSelectsPreviousSection()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("components");

        PresenterNavigationState next = DispatchInput(state, KeyDown(PresenterKey.ArrowUp, ctrl: true));

        Assert.Equal("overview", next.SelectedSectionId);
    }

    [Fact]
    public void KeyboardRoute_CtrlArrowRightSelectsNextTab()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);

        PresenterNavigationState next = DispatchInput(state, KeyDown(PresenterKey.ArrowRight, ctrl: true));

        Assert.Equal("status", next.GetSelectedTabId("overview", Model));
    }

    [Fact]
    public void KeyboardRoute_CtrlArrowLeftSelectsPreviousTab()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedTab("overview", "status");

        PresenterNavigationState next = DispatchInput(state, KeyDown(PresenterKey.ArrowLeft, ctrl: true));

        Assert.Equal("home", next.GetSelectedTabId("overview", Model));
    }

    [Fact]
    public void KeyboardRoute_ClampsScrollOffsets()
    {
        PresenterNavigationState state = ControlsPageState(0);

        PresenterNavigationState top = DispatchInput(state, KeyDown(PresenterKey.ArrowUp));
        PresenterNavigationState bottom = DispatchInput(state, KeyDown(PresenterKey.End));
        PresenterNavigationState beyondBottom = DispatchInput(bottom, KeyDown(PresenterKey.PageDown));
        double expectedBottom = PresenterScrollRegion.ComputeMaxScrollOffset(
            PresenterNavigationCatalog.GetPageContentHeight("components.controls", ProofOptions),
            PresenterNavigationLayout.Default.ViewportHeight);

        Assert.Equal(0, top.GetScrollOffset("components.controls"));
        Assert.Equal(expectedBottom, beyondBottom.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void KeyboardRoute_PreservesPerPageScrollOffsets()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("components")
            .WithSelectedTab("components", "controls")
            .WithScrollOffset("components.controls", 144)
            .WithSelectedSection("overview");

        PresenterNavigationState next = DispatchInput(state, KeyDown(PresenterKey.ArrowDown, ctrl: true));

        Assert.Equal("components", next.SelectedSectionId);
        Assert.Equal(144, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void KeyboardRoute_TextInputDoesNotEditMarkdownInM12g()
    {
        PresenterNavigationState state = DocsPageState("selected-doc-dogfood");

        PresenterNavigationState next = DispatchInput(state, TextInput("edited"));

        Assert.Equal(state.SelectedSectionId, next.SelectedSectionId);
        Assert.Equal(
            state.GetSelectedTabId("oblivion", Model),
            next.GetSelectedTabId("oblivion", Model));
        Assert.Equal(
            state.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId)),
            next.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId)));
        Assert.Equal(state.EffectState, next.EffectState);
    }

    [Fact]
    public void KeyboardRoute_TextInputProducesDeferredDiagnosticOrNoOp()
    {
        PresenterNavigationShellRenderResult render = RenderShell(DocsPageState("selected-doc-dogfood"));

        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(render, TextInput("x"));

        Assert.Null(routed.ActionId);
    }

    [Fact]
    public void KeyboardRoute_CtrlRInvokesDeferredSelectedCardAction()
    {
        PresenterNavigationState state = ExecutionRoadmapState("execution-deferred");

        PresenterNavigationState next = DispatchInput(state, KeyDown(PresenterKey.R, ctrl: true));

        Assert.NotNull(next.EffectState.GetLastRequest(new OblivionCardId("execution-deferred")));
        Assert.NotNull(next.EffectState.GetLastResult(new OblivionCardId("execution-deferred")));
    }

    [Fact]
    public void KeyboardRoute_CardActionStillDoesNotExecute()
    {
        PresenterNavigationState state = ExecutionRoadmapState("execution-deferred");

        PresenterNavigationState next = DispatchInput(state, KeyDown(PresenterKey.R, ctrl: true));
        OblivionCardEffectResult? result = next.EffectState.GetLastResult(new OblivionCardId("execution-deferred"));

        Assert.NotNull(result);
        Assert.Equal(OblivionCardEffectStatus.Deferred, result!.Status);
        Assert.Contains("No Roslyn or xUnit execution occurred.", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PointerWheelScroll_StillWorks()
    {
        PresenterNavigationState state = ControlsPageState(0);
        PresenterNavigationShellRenderResult render = RenderShell(state);

        PresenterNavigationState next = DispatchInput(
            state,
            Wheel(Center(render.ChromeGeometry.ContentViewportRect), -1));

        Assert.Equal(PresenterNavigationInputRouter.ScrollWheelMultiplier, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void ScrollbarDrag_StillWorks()
    {
        PresenterNavigationState state = ControlsPageState(120);
        PresenterNavigationShellRenderResult render = RenderShell(state);
        PresenterInputPoint thumbCenter = Center(render.ScrollbarGeometry.ThumbRect);

        PresenterNavigationState next = DispatchSequence(
            state,
            [
                PointerPress(thumbCenter),
                PointerMove(new PresenterInputPoint(thumbCenter.X, thumbCenter.Y + 80)),
                PointerRelease(new PresenterInputPoint(thumbCenter.X, thumbCenter.Y + 80)),
            ]);

        Assert.True(next.GetScrollOffset("components.controls") > 120);
    }

    [Fact]
    public void OblivionDocsDogfood_StillWorks()
    {
        IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.CreateDocsPageCards();

        Assert.True(cards.Count > 1);
        Assert.Contains(cards, card => string.Equals(card.Id.Value, OblivionDocsDogfoodCatalog.IndexCardId, StringComparison.Ordinal));
        Assert.Contains(cards, card => string.Equals(card.Body.Format.ToString(), "CopelandMarkdown", StringComparison.Ordinal));
    }

    [Fact]
    public void M12g_DoesNotImplementMarkdownEditor()
    {
        PresenterNavigationShellRenderResult render = RenderShell(DocsPageState("selected-doc-dogfood"));

        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(render, TextInput("abc"));

        Assert.Null(routed.ActionId);
    }

    [Fact]
    public void M12g_DoesNotImplementRoslynExecution()
    {
        PresenterNavigationState next = DispatchInput(
            ExecutionRoadmapState("execution-deferred"),
            KeyDown(PresenterKey.R, ctrl: true));
        OblivionCardEffectResult? result = next.EffectState.GetLastResult(new OblivionCardId("execution-deferred"));

        Assert.NotNull(result);
        Assert.Contains("Roslyn", result!.Message, StringComparison.Ordinal);
        Assert.Contains("xUnit", result.Message, StringComparison.Ordinal);
        Assert.Equal(OblivionCardEffectStatus.Deferred, result.Status);
    }

    [Fact]
    public void M12g_DoesNotImplementVisionary()
    {
        Assert.DoesNotContain(
            GetSourceFiles("samples", "Machina.UI", "Machina.Presenter.Sample").Select(File.ReadAllText),
            source => source.Contains("VisionaryEditor", StringComparison.Ordinal));
    }

    [Fact]
    public void PresenterKeyboardInputManifest_RecordsKeyboardBackendSupport()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-keyboard-m12g-tests", Guid.NewGuid().ToString("N"));

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-keyboard-input-overview.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "overview",
                    SelectedTabId: "home",
                    InteractionBackendName: AvaloniaPresenterInputBackend.BackendName),
                StandardTheme.Default);

            string json = File.ReadAllText(result.KeyboardManifestJsonPath!);
            string text = File.ReadAllText(result.KeyboardManifestTextPath!);

            Assert.True(File.Exists(result.KeyboardManifestJsonPath!));
            Assert.True(File.Exists(result.KeyboardManifestTextPath!));
            Assert.Contains("\"milestone\": \"M12g\"", json, StringComparison.Ordinal);
            Assert.Contains("\"keyboardBackendEnabled\": true", json, StringComparison.Ordinal);
            Assert.Contains("kind=presenter-keyboard-input-backend", text, StringComparison.Ordinal);
            Assert.Contains("markdownEditorImplemented=false", text, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    private static PresenterNavigationModel Model => PresenterNavigationCatalog.CreateModel();

    private static PresenterProofOptions ProofOptions => new();

    private static PresenterNavigationState ControlsPageState(double pageOffset)
    {
        return PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("components")
            .WithSelectedTab("components", "controls")
            .WithScrollOffset("components.controls", pageOffset);
    }

    private static PresenterNavigationState DocsPageState(string selectedCardId)
    {
        return PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "docs")
            .WithSelectedCard(OblivionWorkbenchCatalog.DocsPageId, selectedCardId);
    }

    private static PresenterNavigationState ExecutionRoadmapState(string selectedCardId)
    {
        return PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "execution-roadmap")
            .WithSelectedCard(OblivionWorkbenchCatalog.ExecutionRoadmapPageId, selectedCardId);
    }

    private static PresenterNavigationState DispatchInput(
        PresenterNavigationState state,
        PresenterInputEvent inputEvent)
    {
        PresenterNavigationShellRenderResult render = RenderShell(state);
        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(render, inputEvent);
        if (routed.ActionId is null)
        {
            return render.NavigationState;
        }

        return PresenterNavigationDispatch.Dispatch(
            render.NavigationState,
            routed.ActionId.Value,
            Model,
            ProofOptions,
            PresenterNavigationLayout.Default);
    }

    private static PresenterNavigationState DispatchSequence(
        PresenterNavigationState initialState,
        IReadOnlyList<PresenterInputEvent> inputs)
    {
        PresenterNavigationState state = initialState;
        PresenterScrollbarInteractionState interactionState = PresenterScrollbarInteractionState.Default;

        foreach (PresenterInputEvent input in inputs)
        {
            PresenterNavigationShellRenderResult render = RenderShell(state);
            PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(render, input, interactionState);
            interactionState = routed.InteractionState;

            if (routed.ActionId is not null)
            {
                state = PresenterNavigationDispatch.Dispatch(
                    render.NavigationState,
                    routed.ActionId.Value,
                    Model,
                    ProofOptions,
                    PresenterNavigationLayout.Default);
            }
        }

        return state;
    }

    private static PresenterNavigationShellRenderResult RenderShell(PresenterNavigationState state)
    {
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state,
            StandardTheme.Default,
            ProofOptions);
    }

    private static PresenterInputEvent KeyDown(
        PresenterKey key,
        bool ctrl = false,
        bool shift = false,
        bool alt = false,
        bool meta = false,
        bool isRepeat = false)
    {
        return new PresenterInputEvent(
            PresenterInputKind.KeyDown,
            default,
            BackendName: "Test",
            Keyboard: new PresenterKeyboardInput(
                key,
                Text: null,
                new PresenterKeyModifiers(ctrl, shift, alt, meta),
                isRepeat));
    }

    private static PresenterInputEvent TextInput(string text)
    {
        return new PresenterInputEvent(
            PresenterInputKind.TextInput,
            default,
            BackendName: "Test",
            Keyboard: new PresenterKeyboardInput(
                PresenterKey.Unknown,
                text,
                PresenterKeyModifiers.None,
                IsRepeat: false));
    }

    private static PresenterInputEvent PointerPress(PresenterInputPoint point)
    {
        return new PresenterInputEvent(
            PresenterInputKind.PointerPressed,
            point,
            PresenterInputButton.Primary,
            BackendName: "Test");
    }

    private static PresenterInputEvent PointerMove(PresenterInputPoint point)
    {
        return new PresenterInputEvent(
            PresenterInputKind.PointerMoved,
            point,
            PresenterInputButton.Primary,
            BackendName: "Test");
    }

    private static PresenterInputEvent PointerRelease(PresenterInputPoint point)
    {
        return new PresenterInputEvent(
            PresenterInputKind.PointerReleased,
            point,
            PresenterInputButton.Primary,
            BackendName: "Test");
    }

    private static PresenterInputEvent Wheel(PresenterInputPoint point, float deltaY)
    {
        return new PresenterInputEvent(
            PresenterInputKind.Wheel,
            point,
            PresenterInputButton.None,
            deltaY,
            "Test");
    }

    private static PresenterInputPoint Center(Machina.Layout.Geometry.Rect rect)
    {
        return new PresenterInputPoint(
            (float)(rect.X + (rect.Width / 2)),
            (float)(rect.Y + (rect.Height / 2)));
    }

    private static void AssertTypeSurfaceDoesNotReferenceAvalonia(Type type)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            AssertNoAvaloniaType(property.PropertyType);
        }

        foreach (FieldInfo field in type.GetFields(flags))
        {
            AssertNoAvaloniaType(field.FieldType);
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(flags))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                AssertNoAvaloniaType(parameter.ParameterType);
            }
        }

        foreach (MethodInfo method in type.GetMethods(flags))
        {
            if (method.IsSpecialName)
            {
                continue;
            }

            AssertNoAvaloniaType(method.ReturnType);
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                AssertNoAvaloniaType(parameter.ParameterType);
            }
        }
    }

    private static void AssertNoAvaloniaType(Type type)
    {
        if (type == typeof(void))
        {
            return;
        }

        Assert.False(
            type.Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true,
            $"Unexpected Avalonia type reference: {type.FullName}");

        if (type.IsArray)
        {
            AssertNoAvaloniaType(type.GetElementType()!);
            return;
        }

        if (type.IsGenericType)
        {
            foreach (Type genericArgument in type.GetGenericArguments())
            {
                AssertNoAvaloniaType(genericArgument);
            }
        }
    }

    private static IEnumerable<string> GetSourceFiles(params string[] pathParts)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Copeland.slnx")))
        {
            string? parent = Directory.GetParent(root)?.FullName;
            if (parent is null)
            {
                throw new InvalidOperationException("Could not find repo root.");
            }

            root = parent;
        }

        string path = Path.Combine([root, .. pathParts]);
        return Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
