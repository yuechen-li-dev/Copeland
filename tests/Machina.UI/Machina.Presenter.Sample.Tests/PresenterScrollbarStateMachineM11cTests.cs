using System.Reflection;
using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Renderer.Raster.Surface;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PresenterScrollbarStateMachineM11cTests
{
    [Fact]
    public void ScrollbarStateMachine_UsesExplicitStates()
    {
        Assert.False(typeof(PresenterScrollbarInteractionState).IsSealed);
        Assert.Contains(
            typeof(PresenterScrollbarInteractionState).GetNestedTypes(BindingFlags.Public),
            type => type == typeof(PresenterScrollbarInteractionState.Idle));
        Assert.Contains(
            typeof(PresenterScrollbarInteractionState).GetNestedTypes(BindingFlags.Public),
            type => type == typeof(PresenterScrollbarInteractionState.ThumbDragging));
    }

    [Fact]
    public void ScrollbarStateMachine_IdleToThumbDragging_OnThumbPress()
    {
        PresenterNavigationShellRenderResult render = RenderShell(ScrolledPageState(120));

        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
            render,
            PointerPress(Center(render.ScrollbarGeometry.ThumbRect)));

        Assert.IsType<PresenterScrollbarInteractionState.ThumbDragging>(routed.InteractionState);
    }

    [Fact]
    public void ScrollbarStateMachine_ThumbDraggingToIdle_OnRelease()
    {
        PresenterNavigationShellRenderResult render = RenderShell(ScrolledPageState(120));
        PresenterNavigationInputRoutingResult dragStart = PresenterNavigationInputRouter.Route(
            render,
            PointerPress(Center(render.ScrollbarGeometry.ThumbRect)));

        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
            render,
            PointerRelease(Center(render.ScrollbarGeometry.ThumbRect)),
            dragStart.InteractionState);

        Assert.IsType<PresenterScrollbarInteractionState.Idle>(routed.InteractionState);
    }

    [Fact]
    public void ScrollbarStateMachine_SuppressesSidebarAndTabRoutesWhileDragging()
    {
        PresenterNavigationShellRenderResult render = RenderShell(ScrolledPageState(120));
        PresenterNavigationInputRoutingResult dragStart = PresenterNavigationInputRouter.Route(
            render,
            PointerPress(Center(render.ScrollbarGeometry.ThumbRect)));
        PresenterNavigationSidebarHitRegion sidebar = Assert.Single(
            render.ChromeGeometry.SidebarSections,
            item => item.SectionId == "overview");

        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
            render,
            PointerPress(Center(sidebar.Rect)),
            dragStart.InteractionState);

        Assert.True(routed.SuppressFurtherRouting);
        Assert.Null(routed.ActionId);
    }

    [Fact]
    public void ScrollbarStateMachine_RequestsPointerCaptureOnDragStart()
    {
        PresenterNavigationShellRenderResult render = RenderShell(ScrolledPageState(120));

        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
            render,
            PointerPress(Center(render.ScrollbarGeometry.ThumbRect)));

        Assert.Equal(PresenterPointerCaptureRequest.Capture, routed.PointerCaptureRequest);
    }

    [Fact]
    public void ScrollbarStateMachine_RequestsPointerReleaseOnDragEnd()
    {
        PresenterNavigationShellRenderResult render = RenderShell(ScrolledPageState(120));
        PresenterNavigationInputRoutingResult dragStart = PresenterNavigationInputRouter.Route(
            render,
            PointerPress(Center(render.ScrollbarGeometry.ThumbRect)));

        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
            render,
            PointerRelease(Center(render.ScrollbarGeometry.ThumbRect)),
            dragStart.InteractionState);

        Assert.Equal(PresenterPointerCaptureRequest.Release, routed.PointerCaptureRequest);
    }

    [Fact]
    public void ScrollbarStateMachine_DoesNotReferenceAvaloniaTypes()
    {
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterScrollbarInteractionState));
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterScrollbarInteractionStateMachine));
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterNavigationInputRouter));
    }

    [Fact]
    public void ScrollbarDrag_UpdatesScrollOffset()
    {
        PresenterNavigationState state = ScrolledPageState(120);
        PresenterNavigationState next = DispatchSequence(
            state,
            [
                PointerPress(Center(RenderShell(state).ScrollbarGeometry.ThumbRect)),
                PointerMove(OffsetPoint(Center(RenderShell(state).ScrollbarGeometry.ThumbRect), 0, 80)),
                PointerRelease(OffsetPoint(Center(RenderShell(state).ScrollbarGeometry.ThumbRect), 0, 80)),
            ]);

        Assert.True(next.GetScrollOffset("components.controls") > 120);
    }

    [Fact]
    public void ScrollbarDrag_ClampsAtTopAndBottom()
    {
        PresenterNavigationState state = ScrolledPageState(120);
        PresenterNavigationState top = DispatchSequence(
            state,
            [
                PointerPress(Center(RenderShell(state).ScrollbarGeometry.ThumbRect)),
                PointerMove(OffsetPoint(Center(RenderShell(state).ScrollbarGeometry.ThumbRect), 0, -1000)),
                PointerRelease(OffsetPoint(Center(RenderShell(state).ScrollbarGeometry.ThumbRect), 0, -1000)),
            ]);
        PresenterNavigationState bottom = DispatchSequence(
            state,
            [
                PointerPress(Center(RenderShell(state).ScrollbarGeometry.ThumbRect)),
                PointerMove(OffsetPoint(Center(RenderShell(state).ScrollbarGeometry.ThumbRect), 0, 1000)),
                PointerRelease(OffsetPoint(Center(RenderShell(state).ScrollbarGeometry.ThumbRect), 0, 1000)),
            ]);

        double expectedBottom = PresenterScrollRegion.ComputeMaxScrollOffset(
            PresenterNavigationCatalog.GetPageContentHeight("components.controls", ProofOptions),
            PresenterNavigationLayout.Default.ViewportHeight);

        Assert.Equal(0, top.GetScrollOffset("components.controls"));
        Assert.Equal(expectedBottom, bottom.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void ScrollbarTrackClick_PagesUpAndDown()
    {
        PresenterNavigationState state = ScrolledPageState(200);
        PresenterNavigationShellRenderResult render = RenderShell(state);

        PresenterNavigationState up = DispatchInput(state, PointerPress(AboveThumb(render.ScrollbarGeometry)));
        PresenterNavigationState down = DispatchInput(state, PointerPress(BelowThumb(render.ScrollbarGeometry)));

        Assert.True(up.GetScrollOffset("components.controls") < 200);
        Assert.True(down.GetScrollOffset("components.controls") > 200);
    }

    [Fact]
    public void WheelScroll_StillWorks()
    {
        PresenterNavigationState state = ScrolledPageState(0);
        PresenterNavigationShellRenderResult render = RenderShell(state);

        PresenterNavigationState next = DispatchInput(
            state,
            Wheel(Center(render.ChromeGeometry.ContentViewportRect), -1));

        Assert.Equal(PresenterNavigationInputRouter.ScrollWheelMultiplier, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void PerPageScrollOffsets_ArePreserved()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("components")
            .WithSelectedTab("components", "controls")
            .WithScrollOffset("components.controls", 144)
            .WithSelectedTab("components", "cards");

        PresenterNavigationShellRenderResult render = RenderShell(state);
        PresenterNavigationTabHitRegion region = Assert.Single(
            render.ChromeGeometry.LocalTabs,
            item => item.TabId == "controls");

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)));

        Assert.Equal(144, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void ScrollOffsetChange_DoesNotRerenderPageContent()
    {
        var session = new PresenterNavigationRenderSession();
        PresenterNavigationState state = ScrolledPageState(0);

        _ = RenderShell(state, session: session);
        PresenterNavigationShellRenderResult scrolled = RenderShell(
            state.WithScrollOffset("components.controls", 180),
            session: session);

        Assert.Equal(1, scrolled.Diagnostics.PageRenderCount);
    }

    [Fact]
    public void ScrollOffsetChange_DoesNotRerenderShellChrome()
    {
        var session = new PresenterNavigationRenderSession();
        PresenterNavigationState state = ScrolledPageState(0);

        _ = RenderShell(state, session: session);
        PresenterNavigationShellRenderResult scrolled = RenderShell(
            state.WithScrollOffset("components.controls", 180),
            session: session);

        Assert.Equal(1, scrolled.Diagnostics.ShellRenderCount);
    }

    [Fact]
    public void ScrollOffsetChange_RecomposesFrame()
    {
        var session = new PresenterNavigationRenderSession();
        PresenterNavigationState state = ScrolledPageState(0);

        PresenterNavigationShellRenderResult initial = RenderShell(state, session: session);
        PresenterNavigationShellRenderResult scrolled = RenderShell(
            state.WithScrollOffset("components.controls", 180),
            session: session);

        Assert.Equal(1, initial.Diagnostics.CompositionCount);
        Assert.Equal(2, scrolled.Diagnostics.CompositionCount);
    }

    [Fact]
    public void SectionChange_InvalidatesChromeAndPageCache()
    {
        var session = new PresenterNavigationRenderSession();
        _ = RenderShell(ScrolledPageState(0), session: session);

        PresenterNavigationShellRenderResult next = RenderShell(
            PresenterNavigationState.CreateDefault(Model),
            session: session);

        Assert.Equal(2, next.Diagnostics.PageRenderCount);
        Assert.Equal(2, next.Diagnostics.ShellRenderCount);
    }

    [Fact]
    public void TabChange_InvalidatesPageCache()
    {
        var session = new PresenterNavigationRenderSession();
        PresenterNavigationState state = ScrolledPageState(0);
        _ = RenderShell(state, session: session);

        PresenterNavigationShellRenderResult next = RenderShell(
            state.WithSelectedTab("components", "cards"),
            session: session);

        Assert.Equal(2, next.Diagnostics.PageRenderCount);
    }

    [Fact]
    public void Resize_InvalidatesCache()
    {
        var session = new PresenterNavigationRenderSession();
        PresenterNavigationState state = ScrolledPageState(0);
        _ = RenderShell(state, session: session);

        PresenterNavigationLayout resizedLayout = PresenterNavigationLayout.Default with
        {
            RootWidth = PresenterNavigationLayout.Default.RootWidth + 80,
        };
        PresenterNavigationShellRenderResult next = PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state,
            StandardTheme.Default,
            ProofOptions,
            session,
            resizedLayout);

        Assert.Equal(2, next.Diagnostics.PageRenderCount);
        Assert.Equal(2, next.Diagnostics.ShellRenderCount);
    }

    [Fact]
    public void ComponentInteraction_InvalidatesPageCache()
    {
        var session = new PresenterNavigationRenderSession();
        PresenterNavigationState state = ScrolledPageState(0);
        _ = RenderShell(state, DemoState.Default, session);

        DemoState changed = DemoStateDispatch.Dispatch(DemoState.Default, SettingsActions.Increment);
        PresenterNavigationShellRenderResult next = RenderShell(state, changed, session);

        Assert.Equal(2, next.Diagnostics.PageRenderCount);
        Assert.Equal(1, next.Diagnostics.ShellRenderCount);
    }

    [Fact]
    public void ComposeFrame_UsesClampedBlitRect()
    {
        RasterSurface source = CreateSurface(4, 4, Rgba32.White);
        RasterSurface destination = CreateSurface(6, 6, Rgba32.Black);

        BlitRect rect = PresenterNavigationFrameComposer.ComputeBlitRect(
            source,
            destination,
            new Rect(4, 4, 4, 4),
            scrollOffset: 3);

        Assert.Equal(0, rect.SourceX);
        Assert.Equal(3, rect.SourceY);
        Assert.Equal(4, rect.DestinationX);
        Assert.Equal(4, rect.DestinationY);
        Assert.Equal(2, rect.Width);
        Assert.Equal(1, rect.Height);
    }

    [Fact]
    public void ComposeFrame_HandlesNegativeScrollOffsetByClamping()
    {
        RasterSurface source = CreateSurface(4, 4, Rgba32.White);
        RasterSurface destination = CreateSurface(6, 6, Rgba32.Black);

        BlitRect rect = PresenterNavigationFrameComposer.ComputeBlitRect(
            source,
            destination,
            new Rect(1, 1, 3, 3),
            scrollOffset: -12);

        Assert.Equal(0, rect.SourceY);
    }

    [Fact]
    public void ComposeFrame_HandlesViewportLargerThanContent()
    {
        RasterSurface source = CreateSurface(2, 2, Rgba32.White);
        RasterSurface destination = CreateSurface(6, 6, Rgba32.Black);

        BlitRect rect = PresenterNavigationFrameComposer.ComputeBlitRect(
            source,
            destination,
            new Rect(1, 1, 5, 5),
            scrollOffset: 0);

        Assert.Equal(2, rect.Width);
        Assert.Equal(2, rect.Height);
    }

    [Fact]
    public void ComposeFrame_ProducesSamePixelsAsPreviousSafePath()
    {
        RasterFrame shell = CreateFrame(6, 6, Rgba32.Black);
        RasterFrame page = CreateFrame(4, 4, Rgba32.Transparent);

        page.Surface.SetPixel(0, 0, Rgba32.White);
        page.Surface.SetPixel(1, 1, Rgba32.FromRgba(0xFF0000FF));
        page.Surface.SetPixel(2, 2, Rgba32.FromRgba(0x00FF00FF));

        ScrollbarGeometry scrollbar = new(
            new Rect(5, 1, 1, 4),
            new Rect(5, 2, 1, 2),
            IsVisible: false,
            ScrollOffset: 0,
            MaxScrollOffset: 0);

        RasterFrame current = PresenterNavigationFrameComposer.Compose(shell, page, new Rect(1, 1, 4, 4), scrollbar);
        RasterFrame previous = ComposeLegacy(shell, page, new Rect(1, 1, 4, 4), 0);

        Assert.Equal(previous.Surface.Pixels, current.Surface.Pixels);
    }

    private static PresenterNavigationModel Model => PresenterNavigationCatalog.CreateModel();

    private static PresenterProofOptions ProofOptions => new();

    private static PresenterNavigationState ScrolledPageState(double pageOffset)
    {
        return PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("components")
            .WithSelectedTab("components", "controls")
            .WithScrollOffset("components.controls", pageOffset);
    }

    private static PresenterNavigationShellRenderResult RenderShell(
        PresenterNavigationState state,
        DemoState? demoState = null,
        PresenterNavigationRenderSession? session = null)
    {
        return PresenterNavigationShellRenderer.Render(
            demoState ?? DemoState.Default,
            state,
            StandardTheme.Default,
            ProofOptions,
            session);
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

    private static PresenterInputPoint Center(Rect rect)
    {
        return new PresenterInputPoint(
            (float)(rect.X + (rect.Width / 2)),
            (float)(rect.Y + (rect.Height / 2)));
    }

    private static PresenterInputPoint AboveThumb(ScrollbarGeometry geometry)
    {
        return new PresenterInputPoint(
            (float)(geometry.TrackRect.X + (geometry.TrackRect.Width / 2)),
            (float)Math.Max(geometry.TrackRect.Y, geometry.ThumbRect.Y - 24));
    }

    private static PresenterInputPoint BelowThumb(ScrollbarGeometry geometry)
    {
        return new PresenterInputPoint(
            (float)(geometry.TrackRect.X + (geometry.TrackRect.Width / 2)),
            (float)Math.Min(
                geometry.TrackRect.Y + geometry.TrackRect.Height - 1,
                geometry.ThumbRect.Y + geometry.ThumbRect.Height + 24));
    }

    private static PresenterInputPoint OffsetPoint(PresenterInputPoint point, float deltaX, float deltaY)
    {
        return new PresenterInputPoint(point.X + deltaX, point.Y + deltaY);
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

    private static RasterSurface CreateSurface(int width, int height, Rgba32 fill)
    {
        var surface = new RasterSurface(width, height);
        Array.Fill(surface.Pixels, fill);
        return surface;
    }

    private static RasterFrame CreateFrame(int width, int height, Rgba32 fill)
    {
        return new RasterFrame(width, height, CreateSurface(width, height, fill));
    }

    private static RasterFrame ComposeLegacy(
        RasterFrame shellFrame,
        RasterFrame pageFrame,
        Rect viewportRect,
        double scrollOffset)
    {
        var clone = new RasterSurface(shellFrame.Surface.Width, shellFrame.Surface.Height);
        Array.Copy(shellFrame.Surface.Pixels, clone.Pixels, shellFrame.Surface.Pixels.Length);

        int sourceTop = Math.Max(0, (int)Math.Floor(scrollOffset));
        int viewportLeft = (int)Math.Floor(viewportRect.X);
        int viewportTop = (int)Math.Floor(viewportRect.Y);
        int viewportWidth = Math.Min((int)Math.Floor(viewportRect.Width), pageFrame.Surface.Width);
        int viewportHeight = (int)Math.Floor(viewportRect.Height);

        for (int y = 0; y < viewportHeight; y++)
        {
            int sourceY = sourceTop + y;
            if (sourceY < 0 || sourceY >= pageFrame.Surface.Height)
            {
                continue;
            }

            int destinationY = viewportTop + y;
            if (destinationY < 0 || destinationY >= clone.Height)
            {
                continue;
            }

            for (int x = 0; x < viewportWidth; x++)
            {
                int destinationX = viewportLeft + x;
                if (destinationX < 0 || destinationX >= clone.Width)
                {
                    continue;
                }

                Rgba32 pixel = pageFrame.Surface.GetPixel(x, sourceY);
                if (pixel.A == 0)
                {
                    continue;
                }

                clone.SetPixel(destinationX, destinationY, pixel);
            }
        }

        return new RasterFrame(shellFrame.Width, shellFrame.Height, clone);
    }
}
