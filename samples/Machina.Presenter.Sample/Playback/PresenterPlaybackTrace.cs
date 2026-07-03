namespace Machina.Presenter.Sample.Playback;

public sealed record PresenterPlaybackTrace(
    string ScenarioId,
    string ScenarioName,
    IReadOnlyList<PresenterPlaybackTraceStep> Steps,
    IReadOnlyList<PresenterPlaybackAssertionResult> Assertions,
    PresenterPlaybackStateSnapshot InitialState,
    PresenterPlaybackStateSnapshot FinalState);

public sealed record PresenterPlaybackTraceStep(
    int Index,
    string Type,
    string? Target,
    string? CardId,
    PresenterPlaybackTargetResolution? TargetResolution,
    PresenterPlaybackResolvedPoint? ResolvedPoint,
    PresenterPlaybackResolvedRect? ResolvedRect,
    PresenterPlaybackHitTestResult? HitTestResult,
    PresenterPlaybackEmittedInput? EmittedInput,
    PresenterPlaybackDispatchedAction? DispatchedAction,
    PresenterPlaybackStateDelta? StateDelta,
    PresenterPlaybackStateSnapshot Before,
    PresenterPlaybackStateSnapshot After,
    string Result);

public sealed record PresenterPlaybackResolvedPoint(
    double X,
    double Y);

public sealed record PresenterPlaybackResolvedRect(
    double X,
    double Y,
    double Width,
    double Height);

public sealed record PresenterPlaybackEmittedInput(
    string Kind,
    string? Key,
    double? WheelDeltaY,
    string? ActionId,
    string? PointerCaptureRequest,
    bool InputConsumed);

public sealed record PresenterPlaybackTargetResolution(
    string SemanticTargetKind,
    string? RequestedCardId,
    string? ResolvedCardId,
    string? ResolvedRegionKind,
    string? ResolvedRegionId,
    PresenterPlaybackResolvedPoint? ResolvedPoint,
    PresenterPlaybackResolvedRect? ResolvedRect);

public sealed record PresenterPlaybackHitTestResult(
    string RegionKind,
    string? RegionId,
    string? CardId,
    string? ScrollRegionId,
    PresenterPlaybackResolvedPoint LocalPoint);

public sealed record PresenterPlaybackDispatchedAction(
    string? ActionId,
    string ActionType,
    bool ActionHandled,
    bool WheelConsumed);

public sealed record PresenterPlaybackStateDelta(
    double MainStackScrollDelta,
    double InspectorScrollDelta,
    double RawSourceScrollDelta,
    double ExpandedBodyScrollDelta);

public sealed record PresenterPlaybackAssertionResult(
    int Index,
    string Type,
    string Reason,
    string Expected,
    string Actual,
    bool Passed,
    string? FailureMessage);

public sealed record PresenterPlaybackRunResult(
    PresenterPlaybackScenario Scenario,
    PresenterNavigationState FinalState,
    PresenterNavigationShellRenderResult FinalRender,
    PresenterPlaybackTrace Trace,
    string OutputDirectory,
    string? FinalPngPath,
    string? NormalizedScenarioPath,
    string? TraceJsonPath,
    string? ManifestJsonPath,
    string? ManifestTextPath);
