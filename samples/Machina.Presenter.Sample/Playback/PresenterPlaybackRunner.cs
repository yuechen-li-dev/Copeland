using Machina.Core.Actions;
using Machina.Layout.Geometry;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample.Playback;

public sealed class PresenterPlaybackRunner
{
    private readonly DemoState _demoState;
    private readonly StandardTheme _theme;
    private readonly PresenterProofOptions _proofOptions;

    public PresenterPlaybackRunner(
        DemoState? demoState = null,
        StandardTheme? theme = null,
        PresenterProofOptions? proofOptions = null)
    {
        _demoState = demoState ?? DemoState.Default;
        _theme = theme ?? Program.AppTheme;
        _proofOptions = proofOptions ?? new PresenterProofOptions();
    }

    public PresenterPlaybackRunResult RunScenarioFile(string scenarioPath, string? finalPngPath = null)
    {
        PresenterPlaybackScenario scenario = PresenterPlaybackTomlParser.LoadFile(scenarioPath);
        return RunScenario(scenario, finalPngPath);
    }

    public PresenterPlaybackRunResult RunScenario(PresenterPlaybackScenario scenario, string? finalPngPath = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        string resolvedFinalPngPath = ResolveFinalPngPath(scenario.Id, finalPngPath);
        string outputDirectory = Path.GetDirectoryName(resolvedFinalPngPath)
            ?? throw new InvalidOperationException("Playback output path must include a directory.");
        Directory.CreateDirectory(outputDirectory);

        PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
        PresenterNavigationLayout layout = CreateLayout(scenario.Viewport);
        PresenterNavigationState state = CreateInitialState(scenario, model, layout);
        PresenterNavigationRenderSession session = new();
        PresenterScrollbarInteractionState interactionState = PresenterScrollbarInteractionState.Default;
        List<PresenterPlaybackTraceStep> traceSteps = [];

        PresenterNavigationShellRenderResult initialRender = Render(state, layout, session);
        PresenterPlaybackStateSnapshot initialSnapshot = PresenterPlaybackStateSnapshot.Capture(initialRender);

        for (int index = 0; index < scenario.Steps.Count; index++)
        {
            PresenterPlaybackStep step = scenario.Steps[index];
            PresenterNavigationShellRenderResult beforeRender = Render(state, layout, session);
            PresenterPlaybackResolvedTarget? resolvedTarget = ResolveTargetForStep(beforeRender, step);
            PresenterPlaybackStateSnapshot beforeSnapshot = PresenterPlaybackStateSnapshot.Capture(
                beforeRender,
                resolvedTarget?.CardId,
                resolvedTarget?.Name);

            StepExecutionResult execution = ExecuteStep(step, state, beforeRender, resolvedTarget, interactionState);
            state = execution.NextState;
            interactionState = execution.NextInteractionState;

            PresenterNavigationShellRenderResult afterRender = Render(state, layout, session);
            PresenterPlaybackStateSnapshot afterSnapshot = PresenterPlaybackStateSnapshot.Capture(
                afterRender,
                resolvedTarget?.CardId,
                resolvedTarget?.Name);

            traceSteps.Add(
                new PresenterPlaybackTraceStep(
                    Index: index,
                    Type: step.Type,
                    Target: resolvedTarget?.Name,
                    CardId: resolvedTarget?.CardId,
                    ResolvedPoint: resolvedTarget is null ? null : new PresenterPlaybackResolvedPoint(resolvedTarget.Point.X, resolvedTarget.Point.Y),
                    ResolvedRect: resolvedTarget is null ? null : new PresenterPlaybackResolvedRect(
                        resolvedTarget.Bounds.X,
                        resolvedTarget.Bounds.Y,
                        resolvedTarget.Bounds.Width,
                        resolvedTarget.Bounds.Height),
                    EmittedInput: execution.EmittedInput,
                    Before: beforeSnapshot,
                    After: afterSnapshot,
                    Result: execution.Result));
        }

        PresenterNavigationShellRenderResult finalRender = Render(state, layout, session);
        PresenterPlaybackStateSnapshot finalSnapshot = PresenterPlaybackStateSnapshot.Capture(finalRender);
        IReadOnlyList<PresenterPlaybackAssertionResult> assertionResults = EvaluateAssertions(
            scenario.Assertions,
            initialSnapshot,
            finalRender);

        PresenterPlaybackTrace trace = new(
            ScenarioId: scenario.Id,
            ScenarioName: scenario.Name,
            Steps: traceSteps,
            Assertions: assertionResults,
            InitialState: initialSnapshot,
            FinalState: finalSnapshot);

        string normalizedScenarioPath = PresenterPlaybackOutputWriter.WriteNormalizedScenario(outputDirectory, scenario);
        string? traceJsonPath = scenario.Output.CaptureTraceJson
            ? PresenterPlaybackOutputWriter.WriteTraceJson(outputDirectory, trace)
            : null;

        string? actualFinalPngPath = null;
        if (scenario.Output.CaptureFinalPng)
        {
            PresenterPngWriter.Write(resolvedFinalPngPath, finalRender.ComposedFrame);
            actualFinalPngPath = resolvedFinalPngPath;
        }

        var provisionalResult = new PresenterPlaybackRunResult(
            Scenario: scenario,
            FinalState: state,
            FinalRender: finalRender,
            Trace: trace,
            OutputDirectory: outputDirectory,
            FinalPngPath: actualFinalPngPath,
            NormalizedScenarioPath: normalizedScenarioPath,
            TraceJsonPath: traceJsonPath,
            ManifestJsonPath: null,
            ManifestTextPath: null);

        string? manifestJsonPath = null;
        string? manifestTextPath = null;
        if (scenario.Output.CaptureManifest)
        {
            (manifestJsonPath, manifestTextPath) = PresenterPlaybackOutputWriter.WriteManifest(outputDirectory, provisionalResult);
        }

        return provisionalResult with
        {
            ManifestJsonPath = manifestJsonPath,
            ManifestTextPath = manifestTextPath,
        };
    }

    private PresenterNavigationState CreateInitialState(
        PresenterPlaybackScenario scenario,
        PresenterNavigationModel model,
        PresenterNavigationLayout layout)
    {
        PresenterNavigationSection section = model.FindSection(scenario.Section)
            ?? throw new InvalidOperationException($"Scenario section '{scenario.Section}' is not a valid presenter section.");
        PresenterNavigationTab tab = model.FindTab(section.Id, scenario.Tab)
            ?? throw new InvalidOperationException($"Scenario tab '{scenario.Tab}' is not valid for section '{section.Id}'.");
        string pageId = tab.PageId;

        ValidateInitialStateSupport(scenario, pageId);

        PresenterNavigationState state = PresenterNavigationState.CreateDefault(model)
            .WithSelectedSection(section.Id)
            .WithSelectedTab(section.Id, tab.Id);

        string? selectedCardId = null;
        if (!string.IsNullOrWhiteSpace(scenario.SelectedCard))
        {
            selectedCardId = OblivionWorkbenchCatalog.ResolveCardSelectionId(pageId, scenario.SelectedCard, _proofOptions);
            state = state.WithSelectedCard(pageId, selectedCardId);
        }

        if (scenario.MainStackScroll is not null)
        {
            state = state.WithScrollOffset(pageId, scenario.MainStackScroll.Value);
        }

        if (!string.IsNullOrWhiteSpace(scenario.ExpandedCard))
        {
            string expandedCardId = OblivionWorkbenchCatalog.ResolveCardSelectionId(pageId, scenario.ExpandedCard, _proofOptions);
            state = state
                .WithSelectedCard(pageId, expandedCardId)
                .WithCardViewState(
                    pageId,
                    expandedCardId,
                    new OblivionCardViewState(
                        IsExpanded: true,
                        BodyScrollOffset: scenario.ExpandedCardBodyScroll ?? 0));
            selectedCardId = expandedCardId;
        }
        else if (scenario.ExpandedCardBodyScroll is not null)
        {
            throw new InvalidOperationException("Scenario expandedCardBodyScroll requires expandedCard.");
        }

        if (scenario.InspectorScroll is not null)
        {
            state = state.WithInspectorScrollOffset(pageId, scenario.InspectorScroll.Value);
        }

        if (scenario.InspectorRawSourceScroll is not null)
        {
            if (string.IsNullOrWhiteSpace(selectedCardId))
            {
                throw new InvalidOperationException("Scenario inspectorRawSourceScroll requires a selectedCard or expandedCard.");
            }

            state = state.WithRawMarkdownSourceScrollOffset(selectedCardId, scenario.InspectorRawSourceScroll.Value);
        }

        return state;
    }

    private void ValidateInitialStateSupport(PresenterPlaybackScenario scenario, string pageId)
    {
        bool isOblivionPage = PresenterNavigationCatalog.IsOblivionPage(pageId);
        if (!isOblivionPage &&
            (scenario.SelectedCard is not null ||
             scenario.ExpandedCard is not null ||
             scenario.ExpandedCardBodyScroll is not null ||
             scenario.InspectorScroll is not null ||
             scenario.InspectorRawSourceScroll is not null ||
             scenario.MainStackScroll is not null))
        {
            throw new InvalidOperationException(
                "Scenario initial state requests Oblivion-only fields, but the selected presenter page is not an Oblivion page.");
        }
    }

    private StepExecutionResult ExecuteStep(
        PresenterPlaybackStep step,
        PresenterNavigationState state,
        PresenterNavigationShellRenderResult render,
        PresenterPlaybackResolvedTarget? resolvedTarget,
        PresenterScrollbarInteractionState interactionState)
    {
        return step switch
        {
            PresenterPlaybackWaitStep wait => new StepExecutionResult(
                state,
                interactionState,
                new PresenterPlaybackEmittedInput("wait", null, null, null, null),
                $"wait:{wait.Milliseconds}ms"),
            PresenterPlaybackClickStep => ExecuteInputSequence(
                state,
                render,
                interactionState,
                [
                    new PresenterInputEvent(
                        PresenterInputKind.PointerPressed,
                        GetRequiredPoint(resolvedTarget, "click"),
                        PresenterInputButton.Primary,
                        BackendName: "MachinaPlayback"),
                    new PresenterInputEvent(
                        PresenterInputKind.PointerReleased,
                        GetRequiredPoint(resolvedTarget, "click"),
                        PresenterInputButton.Primary,
                        BackendName: "MachinaPlayback"),
                ]),
            PresenterPlaybackWheelStep wheel => ExecuteInputSequence(
                state,
                render,
                interactionState,
                [
                    new PresenterInputEvent(
                        PresenterInputKind.Wheel,
                        GetRequiredPoint(resolvedTarget, "wheel"),
                        WheelDeltaY: NormalizeWheelDelta(wheel.DeltaY),
                        BackendName: "MachinaPlayback"),
                ]),
            PresenterPlaybackKeyStep key => ExecuteInputSequence(
                state,
                render,
                interactionState,
                [
                    new PresenterInputEvent(
                        PresenterInputKind.KeyDown,
                        default,
                        BackendName: "MachinaPlayback",
                        Keyboard: new PresenterKeyboardInput(
                            key.Key,
                            Text: null,
                            PresenterKeyModifiers.None,
                            IsRepeat: false)),
                ]),
            PresenterPlaybackDragStep drag => ExecuteDragStep(state, render, interactionState, drag, resolvedTarget),
            _ => throw new InvalidOperationException($"Unsupported playback step type '{step.Type}'."),
        };
    }

    private StepExecutionResult ExecuteDragStep(
        PresenterNavigationState state,
        PresenterNavigationShellRenderResult render,
        PresenterScrollbarInteractionState interactionState,
        PresenterPlaybackDragStep drag,
        PresenterPlaybackResolvedTarget? resolvedTarget)
    {
        if (resolvedTarget?.ScrollbarGeometry is not null &&
            drag.FromNormalized is not null &&
            drag.ToNormalized is not null)
        {
            Rect thumbRect = resolvedTarget.ScrollbarGeometry.ThumbRect;
            Rect trackRect = resolvedTarget.ScrollbarGeometry.TrackRect;
            PresenterPlaybackPoint start = new(
                thumbRect.X + (thumbRect.Width / 2),
                trackRect.Y + (trackRect.Height * drag.FromNormalized.Value));
            PresenterPlaybackPoint end = new(
                thumbRect.X + (thumbRect.Width / 2),
                trackRect.Y + (trackRect.Height * drag.ToNormalized.Value));

            if (!Contains(thumbRect, start))
            {
                throw new InvalidOperationException(
                    $"Playback drag start for target '{drag.Target}' does not intersect the current scrollbar thumb.");
            }

            return ExecuteInputSequence(
                state,
                render,
                interactionState,
                [
                    new PresenterInputEvent(
                        PresenterInputKind.PointerPressed,
                        start.ToInputPoint(),
                        PresenterInputButton.Primary,
                        BackendName: "MachinaPlayback"),
                    new PresenterInputEvent(
                        PresenterInputKind.PointerMoved,
                        end.ToInputPoint(),
                        PresenterInputButton.Primary,
                        BackendName: "MachinaPlayback"),
                    new PresenterInputEvent(
                        PresenterInputKind.PointerReleased,
                        end.ToInputPoint(),
                        PresenterInputButton.Primary,
                        BackendName: "MachinaPlayback"),
                ]);
        }

        if (drag.FromPoint is not null && drag.ToPoint is not null)
        {
            return ExecuteInputSequence(
                state,
                render,
                interactionState,
                [
                    new PresenterInputEvent(
                        PresenterInputKind.PointerPressed,
                        drag.FromPoint.Value.ToInputPoint(),
                        PresenterInputButton.Primary,
                        BackendName: "MachinaPlayback"),
                    new PresenterInputEvent(
                        PresenterInputKind.PointerMoved,
                        drag.ToPoint.Value.ToInputPoint(),
                        PresenterInputButton.Primary,
                        BackendName: "MachinaPlayback"),
                    new PresenterInputEvent(
                        PresenterInputKind.PointerReleased,
                        drag.ToPoint.Value.ToInputPoint(),
                        PresenterInputButton.Primary,
                        BackendName: "MachinaPlayback"),
                ]);
        }

        throw new InvalidOperationException(
            $"Playback drag target '{drag.Target}' requires either normalized from/to positions or explicit from/to points.");
    }

    private StepExecutionResult ExecuteInputSequence(
        PresenterNavigationState state,
        PresenterNavigationShellRenderResult initialRender,
        PresenterScrollbarInteractionState initialInteractionState,
        IReadOnlyList<PresenterInputEvent> events)
    {
        PresenterNavigationState currentState = state;
        PresenterScrollbarInteractionState currentInteractionState = initialInteractionState;
        PresenterNavigationShellRenderResult currentRender = initialRender;
        PresenterPlaybackEmittedInput? lastInput = null;

        foreach (PresenterInputEvent inputEvent in events)
        {
            PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
                currentRender,
                inputEvent,
                currentInteractionState);
            currentInteractionState = routed.InteractionState;

            UiActionId? actionId = routed.ActionId;
            if (actionId is not null)
            {
                currentState = PresenterNavigationDispatch.Dispatch(
                    currentRender.NavigationState,
                    actionId.Value,
                    currentRender.Model,
                    _proofOptions,
                    currentRender.Layout);
            }

            lastInput = new PresenterPlaybackEmittedInput(
                Kind: inputEvent.Kind.ToString(),
                Key: inputEvent.Keyboard?.Key.ToString(),
                WheelDeltaY: inputEvent.Kind == PresenterInputKind.Wheel ? inputEvent.WheelDeltaY : null,
                ActionId: actionId?.Value,
                PointerCaptureRequest: routed.PointerCaptureRequest.ToString());

            currentRender = Render(currentState, currentRender.Layout, currentRender.Session);
        }

        return new StepExecutionResult(
            currentState,
            currentInteractionState,
            lastInput,
            "ok");
    }

    private IReadOnlyList<PresenterPlaybackAssertionResult> EvaluateAssertions(
        IReadOnlyList<PresenterPlaybackAssertion> assertions,
        PresenterPlaybackStateSnapshot initialSnapshot,
        PresenterNavigationShellRenderResult finalRender)
    {
        List<PresenterPlaybackAssertionResult> results = [];

        for (int index = 0; index < assertions.Count; index++)
        {
            PresenterPlaybackAssertion assertion = assertions[index];
            results.Add(EvaluateAssertion(assertion, index, initialSnapshot, finalRender));
        }

        return results;
    }

    private PresenterPlaybackAssertionResult EvaluateAssertion(
        PresenterPlaybackAssertion assertion,
        int index,
        PresenterPlaybackStateSnapshot initialSnapshot,
        PresenterNavigationShellRenderResult finalRender)
    {
        PresenterPlaybackStateSnapshot finalSnapshot = PresenterPlaybackStateSnapshot.Capture(finalRender);
        string pageId = finalRender.SelectedTab.PageId;

        return assertion switch
        {
            PresenterPlaybackSelectedCardAssertion selectedCard => BuildAssertionResult(
                index,
                assertion,
                expected: selectedCard.Value,
                actual: finalSnapshot.SelectedCard ?? "<none>",
                passed: string.Equals(finalSnapshot.SelectedCard, selectedCard.Value, StringComparison.Ordinal),
                failureMessage: $"Expected selected card '{selectedCard.Value}', but found '{finalSnapshot.SelectedCard ?? "<none>"}'."),
            PresenterPlaybackCardExpandedAssertion expanded => BuildAssertionResult(
                index,
                assertion,
                expected: expanded.Value.ToString(),
                actual: finalRender.NavigationState.GetCardViewState(pageId, expanded.CardId).IsExpanded.ToString(),
                passed: finalRender.NavigationState.GetCardViewState(pageId, expanded.CardId).IsExpanded == expanded.Value,
                failureMessage: $"Expected card '{expanded.CardId}' expanded={expanded.Value}, but found expanded={finalRender.NavigationState.GetCardViewState(pageId, expanded.CardId).IsExpanded}."),
            PresenterPlaybackScrollOffsetChangedAssertion changed => BuildScrollChangedAssertion(index, changed, initialSnapshot, finalRender),
            PresenterPlaybackScrollOffsetGreaterThanAssertion greaterThan => BuildNumericAssertion(
                index,
                assertion,
                expected: $"> {greaterThan.Value:0.###}",
                actualValue: GetScrollOffset(finalRender, greaterThan.Target, greaterThan.CardId),
                passed: actual => actual > greaterThan.Value,
                failureMessageFactory: actual => $"Expected scroll offset for '{greaterThan.Target}' to be greater than {greaterThan.Value:0.###}, but found {actual:0.###}."),
            PresenterPlaybackScrollOffsetEqualsAssertion equalsAssertion => BuildNumericAssertion(
                index,
                assertion,
                expected: equalsAssertion.Value.ToString("0.###"),
                actualValue: GetScrollOffset(finalRender, equalsAssertion.Target, equalsAssertion.CardId),
                passed: actual => Math.Abs(actual - equalsAssertion.Value) < 0.001,
                failureMessageFactory: actual => $"Expected scroll offset for '{equalsAssertion.Target}' to equal {equalsAssertion.Value:0.###}, but found {actual:0.###}."),
            PresenterPlaybackShellModeAssertion shellMode => BuildAssertionResult(
                index,
                assertion,
                expected: shellMode.Value.ToString(),
                actual: finalRender.ShellMode.ToString(),
                passed: finalRender.ShellMode == shellMode.Value,
                failureMessage: $"Expected shell mode '{shellMode.Value}', but found '{finalRender.ShellMode}'."),
            PresenterPlaybackRegionExistsAssertion regionExists => BuildRegionExistsAssertion(index, regionExists, finalRender),
            _ => throw new InvalidOperationException($"Unsupported playback assertion type '{assertion.Type}'."),
        };
    }

    private PresenterPlaybackAssertionResult BuildScrollChangedAssertion(
        int index,
        PresenterPlaybackScrollOffsetChangedAssertion assertion,
        PresenterPlaybackStateSnapshot initialSnapshot,
        PresenterNavigationShellRenderResult finalRender)
    {
        double initialValue = GetScrollOffset(initialSnapshot, assertion.Target);
        double actualValue = GetScrollOffset(finalRender, assertion.Target, assertion.CardId);
        return BuildAssertionResult(
            index,
            assertion,
            expected: $"!= {initialValue:0.###}",
            actual: actualValue.ToString("0.###"),
            passed: Math.Abs(actualValue - initialValue) > 0.001,
            failureMessage: $"Expected scroll offset for '{assertion.Target}' to change from {initialValue:0.###}, but it remained {actualValue:0.###}.");
    }

    private PresenterPlaybackAssertionResult BuildNumericAssertion(
        int index,
        PresenterPlaybackAssertion assertion,
        string expected,
        double actualValue,
        Func<double, bool> passed,
        Func<double, string> failureMessageFactory)
    {
        bool assertionPassed = passed(actualValue);
        return BuildAssertionResult(
            index,
            assertion,
            expected,
            actualValue.ToString("0.###"),
            assertionPassed,
            failureMessageFactory(actualValue));
    }

    private PresenterPlaybackAssertionResult BuildRegionExistsAssertion(
        int index,
        PresenterPlaybackRegionExistsAssertion assertion,
        PresenterNavigationShellRenderResult finalRender)
    {
        try
        {
            PresenterPlaybackResolvedTarget resolved = PresenterPlaybackTargetResolver.Resolve(
                finalRender,
                assertion.Target,
                assertion.CardId);
            return BuildAssertionResult(
                index,
                assertion,
                expected: "exists",
                actual: $"{resolved.Bounds.Width:0.###}x{resolved.Bounds.Height:0.###}",
                passed: true,
                failureMessage: string.Empty);
        }
        catch (Exception ex)
        {
            return BuildAssertionResult(
                index,
                assertion,
                expected: "exists",
                actual: "<missing>",
                passed: false,
                failureMessage: ex.Message);
        }
    }

    private PresenterPlaybackAssertionResult BuildAssertionResult(
        int index,
        PresenterPlaybackAssertion assertion,
        string expected,
        string actual,
        bool passed,
        string failureMessage)
    {
        return new PresenterPlaybackAssertionResult(
            Index: index,
            Type: assertion.Type,
            Reason: assertion.Reason,
            Expected: expected,
            Actual: actual,
            Passed: passed,
            FailureMessage: passed ? null : failureMessage);
    }

    private PresenterPlaybackResolvedTarget? ResolveTargetForStep(
        PresenterNavigationShellRenderResult render,
        PresenterPlaybackStep step)
    {
        return step switch
        {
            PresenterPlaybackClickStep click when click.Point is not null => new PresenterPlaybackResolvedTarget(
                "point",
                click.CardId,
                new Rect(click.Point.Value.X, click.Point.Value.Y, 1, 1),
                click.Point.Value,
                null,
                null),
            PresenterPlaybackClickStep click when !string.IsNullOrWhiteSpace(click.Target) => PresenterPlaybackTargetResolver.Resolve(render, click.Target!, click.CardId),
            PresenterPlaybackWheelStep wheel => PresenterPlaybackTargetResolver.Resolve(render, wheel.Target, wheel.CardId),
            PresenterPlaybackDragStep drag => PresenterPlaybackTargetResolver.Resolve(render, drag.Target, drag.CardId),
            _ => null,
        };
    }

    private PresenterNavigationShellRenderResult Render(
        PresenterNavigationState state,
        PresenterNavigationLayout layout,
        PresenterNavigationRenderSession session)
    {
        return PresenterNavigationShellRenderer.Render(
            _demoState,
            state,
            _theme,
            _proofOptions,
            session,
            layout);
    }

    private PresenterNavigationLayout CreateLayout(PresenterPlaybackViewport viewport)
    {
        PresenterShellMode shellMode = PresenterShellModeResolver.Resolve(viewport.Width);
        return PresenterNavigationLayout.Create(viewport.Width, viewport.Height, shellMode);
    }

    private static PresenterInputPoint GetRequiredPoint(PresenterPlaybackResolvedTarget? target, string stepType)
    {
        if (target is null)
        {
            throw new InvalidOperationException($"Playback step '{stepType}' requires a target or explicit point.");
        }

        return target.Point.ToInputPoint();
    }

    private static string ResolveFinalPngPath(string scenarioId, string? finalPngPath)
    {
        if (!string.IsNullOrWhiteSpace(finalPngPath))
        {
            return Path.GetFullPath(finalPngPath);
        }

        return Path.GetFullPath(Path.Combine("artifacts", "m16a", "playback", scenarioId, "final.png"));
    }

    private static double GetScrollOffset(PresenterNavigationShellRenderResult render, string target, string? cardId)
    {
        string pageId = render.SelectedTab.PageId;
        return target switch
        {
            "main-stack" => render.NavigationState.GetScrollOffset(pageId),
            "expanded-body" => render.NavigationState.GetCardViewState(
                pageId,
                ResolveCardId(render, cardId, target)).BodyScrollOffset,
            "inspector-pane" => render.NavigationState.GetInspectorScrollOffset(pageId),
            "raw-source" => render.NavigationState.GetRawMarkdownSourceScrollOffset(
                ResolveCardId(render, cardId, target)),
            _ => throw new InvalidOperationException($"Scroll offset target '{target}' is not supported."),
        };
    }

    private static double GetScrollOffset(PresenterPlaybackStateSnapshot snapshot, string target)
    {
        return target switch
        {
            "main-stack" => snapshot.MainStackScrollOffset,
            "expanded-body" => snapshot.ExpandedBodyScrollOffset ?? 0,
            "inspector-pane" => snapshot.InspectorScrollOffset,
            "raw-source" => snapshot.RawSourceScrollOffset ?? 0,
            _ => throw new InvalidOperationException($"Scroll offset target '{target}' is not supported."),
        };
    }

    private static string ResolveCardId(PresenterNavigationShellRenderResult render, string? cardId, string target)
    {
        if (!string.IsNullOrWhiteSpace(cardId))
        {
            return cardId;
        }

        IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.GetPageCardsForSelection(render.SelectedTab.PageId, render.ProofOptions);
        return render.NavigationState.GetSelectedCardId(render.SelectedTab.PageId, cards)
            ?? throw new InvalidOperationException($"Target '{target}' requires a selected card.");
    }

    private static bool Contains(Rect rect, PresenterPlaybackPoint point)
    {
        return point.X >= rect.X &&
               point.Y >= rect.Y &&
               point.X < rect.X + rect.Width &&
               point.Y < rect.Y + rect.Height;
    }

    private static float NormalizeWheelDelta(double deltaY)
    {
        if (deltaY > 0)
        {
            return -1;
        }

        if (deltaY < 0)
        {
            return 1;
        }

        return 0;
    }

    private sealed record StepExecutionResult(
        PresenterNavigationState NextState,
        PresenterScrollbarInteractionState NextInteractionState,
        PresenterPlaybackEmittedInput? EmittedInput,
        string Result);
}
