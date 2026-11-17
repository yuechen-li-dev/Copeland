using Aurelian.Machina;
using Machina.Runtime.Input;
using Silk.NET.Input;
using Xunit;

namespace Aurelian.Machina.Tests;

public sealed class AurelianWidgetInputTests
{
    [Theory]
    [InlineData(global::Machina.Core.Semantics.UiCursor.Hand, StandardCursor.Hand)]
    [InlineData(global::Machina.Core.Semantics.UiCursor.IBeam, StandardCursor.IBeam)]
    [InlineData(global::Machina.Core.Semantics.UiCursor.ResizeHorizontal, StandardCursor.HResize)]
    [InlineData(global::Machina.Core.Semantics.UiCursor.ResizeVertical, StandardCursor.VResize)]
    [InlineData(global::Machina.Core.Semantics.UiCursor.Move, StandardCursor.ResizeAll)]
    [InlineData(global::Machina.Core.Semantics.UiCursor.Arrow, StandardCursor.Arrow)]
    public void CursorIntentMapsToNativeShape(global::Machina.Core.Semantics.UiCursor intent, StandardCursor expected)
    {
        Assert.Equal(expected, AurelianCursor.Map(intent));
    }

    [Theory]
    [InlineData(Key.Up, UiKey.ArrowUp)]
    [InlineData(Key.Down, UiKey.ArrowDown)]
    [InlineData(Key.Left, UiKey.ArrowLeft)]
    [InlineData(Key.Right, UiKey.ArrowRight)]
    [InlineData(Key.Enter, UiKey.Enter)]
    [InlineData(Key.Escape, UiKey.Escape)]
    [InlineData(Key.Space, UiKey.Space)]
    [InlineData(Key.Tab, UiKey.Tab)]
    [InlineData(Key.Backspace, UiKey.Backspace)]
    [InlineData(Key.Delete, UiKey.Delete)]
    [InlineData(Key.Z, UiKey.Z)]
    [InlineData(Key.Y, UiKey.Y)]
    [InlineData(Key.F, UiKey.F)]
    [InlineData(Key.R, UiKey.R)]
    [InlineData(Key.O, UiKey.O)]
    [InlineData(Key.E, UiKey.E)]
    [InlineData(Key.F12, UiKey.Unknown)]
    public void MapsNativeKeysWithoutConvertingUnknownKeysToText(Key native, UiKey expected)
    {
        Assert.Equal(expected, AurelianWidgetInput.MapKey(native));
    }
}
