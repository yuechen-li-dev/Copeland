using Machina.Layout.Geometry;
using Machina.Runtime.Input;

namespace Machina.Presenter.Sample.Playback;

public sealed record PresenterPlaybackScenario(
    string SourcePath,
    string Id,
    string Name,
    PresenterPlaybackViewport Viewport,
    string Section,
    string Tab,
    string? SelectedCard,
    string? ExpandedCard,
    double? ExpandedCardBodyScroll,
    double? InspectorScroll,
    double? InspectorRawSourceScroll,
    double? MainStackScroll,
    PresenterPlaybackOutputOptions Output,
    IReadOnlyList<PresenterPlaybackStep> Steps,
    IReadOnlyList<PresenterPlaybackAssertion> Assertions);

public sealed record PresenterPlaybackViewport(
    int Width,
    int Height);

public sealed record PresenterPlaybackOutputOptions(
    bool CaptureFinalPng,
    bool CaptureTraceJson,
    bool CaptureManifest);

public abstract record PresenterPlaybackStep(string Type);

public sealed record PresenterPlaybackWaitStep(int Milliseconds)
    : PresenterPlaybackStep("wait");

public sealed record PresenterPlaybackClickStep(
    string? Target,
    string? CardId,
    PresenterPlaybackPoint? Point)
    : PresenterPlaybackStep("click");

public sealed record PresenterPlaybackWheelStep(
    string Target,
    string? CardId,
    double DeltaY)
    : PresenterPlaybackStep("wheel");

public sealed record PresenterPlaybackKeyStep(
    UiKey Key)
    : PresenterPlaybackStep("key");

public sealed record PresenterPlaybackDragStep(
    string Target,
    string? CardId,
    double? FromNormalized,
    double? ToNormalized,
    PresenterPlaybackPoint? FromPoint,
    PresenterPlaybackPoint? ToPoint)
    : PresenterPlaybackStep("drag");

public readonly record struct PresenterPlaybackPoint(
    double X,
    double Y)
{
    public PointerPoint ToInputPoint()
    {
        return new PointerPoint(X, Y);
    }
}

public abstract record PresenterPlaybackAssertion(string Type, string Reason);

public sealed record PresenterPlaybackSelectedCardAssertion(
    string Value,
    string Reason)
    : PresenterPlaybackAssertion("selected-card", Reason);

public sealed record PresenterPlaybackCardExpandedAssertion(
    string CardId,
    bool Value,
    string Reason)
    : PresenterPlaybackAssertion("card-expanded", Reason);

public sealed record PresenterPlaybackScrollOffsetChangedAssertion(
    string Target,
    string? CardId,
    string Reason)
    : PresenterPlaybackAssertion("scroll-offset-changed", Reason);

public sealed record PresenterPlaybackScrollOffsetGreaterThanAssertion(
    string Target,
    string? CardId,
    double Value,
    string Reason)
    : PresenterPlaybackAssertion("scroll-offset-greater-than", Reason);

public sealed record PresenterPlaybackScrollOffsetEqualsAssertion(
    string Target,
    string? CardId,
    double Value,
    string Reason)
    : PresenterPlaybackAssertion("scroll-offset-equals", Reason);

public sealed record PresenterPlaybackShellModeAssertion(
    PresenterShellMode Value,
    string Reason)
    : PresenterPlaybackAssertion("shell-mode", Reason);

public sealed record PresenterPlaybackRegionExistsAssertion(
    string Target,
    string? CardId,
    string Reason)
    : PresenterPlaybackAssertion("region-exists", Reason);

public sealed record PresenterPlaybackStepScrollDeltaGreaterThanAssertion(
    int Step,
    string Target,
    string? CardId,
    double Value,
    string Reason)
    : PresenterPlaybackAssertion("step-scroll-delta-greater-than", Reason);

public sealed record PresenterPlaybackStepScrollDeltaEqualsAssertion(
    int Step,
    string Target,
    string? CardId,
    double Value,
    string Reason)
    : PresenterPlaybackAssertion("step-scroll-delta-equals", Reason);

public sealed record PresenterPlaybackResolvedTarget(
    string Name,
    string? CardId,
    Rect Bounds,
    PresenterPlaybackPoint Point,
    OblivionScrollTarget? ScrollbarTarget,
    ScrollbarGeometry? ScrollbarGeometry,
    string? ResolvedRegionKind = null,
    string? ResolvedRegionId = null);
