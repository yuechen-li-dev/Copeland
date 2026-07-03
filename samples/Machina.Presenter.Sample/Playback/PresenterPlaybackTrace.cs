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
    PresenterPlaybackResolvedPoint? ResolvedPoint,
    PresenterPlaybackResolvedRect? ResolvedRect,
    PresenterPlaybackEmittedInput? EmittedInput,
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
    string? PointerCaptureRequest);

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
