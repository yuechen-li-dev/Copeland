using Dominatus.Core.Runtime;
using Machina.Core.Styling;
using Machina.Layout.Geometry;

namespace Machina.Dominatus.Rendering.Commands;

public sealed record BeginFrameCommand(
    int Width,
    int Height) : IActuationCommand;

public sealed record EndFrameCommand : IActuationCommand;

public sealed record FillRectCommand(
    string Id,
    Rect Rect,
    ColorToken Color) : IActuationCommand;

public sealed record StrokeRectCommand(
    string Id,
    Rect Rect,
    ColorToken Color,
    double Thickness) : IActuationCommand;

public sealed record DrawTextCommand(
    string Id,
    Rect Rect,
    string Text,
    TextStyle Style) : IActuationCommand;

public sealed record PushClipCommand(
    string Id,
    Rect Rect) : IActuationCommand;

public sealed record PopClipCommand : IActuationCommand;
