using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Oblivion.Product;

namespace Oblivion.Avalonia;

public sealed class AvaloniaOblivionDiagramCanvas : Border
{
    private readonly Control _world;
    private readonly double _worldWidth;
    private readonly double _worldHeight;
    private readonly Action<OblivionDiagramViewportState>? _stateChanged;
    private OblivionDiagramViewportState _state;
    private Point? _panAnchor;

    public AvaloniaOblivionDiagramCanvas(
        Bitmap bitmap,
        OblivionDiagramViewportState state,
        Action<OblivionDiagramViewportState>? stateChanged = null)
        : this(
            new Image
            {
                Source = bitmap,
                Width = bitmap.Size.Width,
                Height = bitmap.Size.Height,
                Stretch = Stretch.Fill,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            },
            bitmap.Size.Width,
            bitmap.Size.Height,
            state,
            stateChanged)
    {
    }

    public AvaloniaOblivionDiagramCanvas(
        Control world,
        double worldWidth,
        double worldHeight,
        OblivionDiagramViewportState state,
        Action<OblivionDiagramViewportState>? stateChanged = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (worldWidth <= 0 || worldHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldWidth), "Diagram world bounds must be positive.");
        }

        _state = state;
        _stateChanged = stateChanged;
        _world = world;
        _worldWidth = worldWidth;
        _worldHeight = worldHeight;
        ClipToBounds = true;
        Focusable = true;
        _world.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        Child = _world;
        SizeChanged += (_, _) => UpdateCamera();
        PointerWheelChanged += HandlePointerWheelChanged;
        PointerPressed += HandlePointerPressed;
        PointerMoved += HandlePointerMoved;
        PointerReleased += HandlePointerReleased;
        PointerCaptureLost += (_, _) => _panAnchor = null;
    }

    public OblivionDiagramViewportState ViewState => _state;

    public OblivionDiagramCamera Camera { get; private set; } =
        OblivionDiagramCameraMath.Resolve(OblivionDiagramViewportState.Fit, 1, 1, 1, 1);

    public void SetViewState(OblivionDiagramViewportState state)
    {
        _state = state;
        UpdateCamera();
    }

    private void HandlePointerWheelChanged(object? sender, PointerWheelEventArgs args)
    {
        if ((args.KeyModifiers & KeyModifiers.Control) == 0)
        {
            return;
        }

        double factor = args.Delta.Y > 0
            ? OblivionDiagramViewportState.ZoomStep
            : 1 / OblivionDiagramViewportState.ZoomStep;
        SetState(_state.ZoomBy(factor));
        args.Handled = true;
    }

    private void HandlePointerPressed(object? sender, PointerPressedEventArgs args)
    {
        PointerPoint point = args.GetCurrentPoint(this);
        if (!point.Properties.IsMiddleButtonPressed)
        {
            return;
        }

        _panAnchor = point.Position;
        args.Pointer.Capture(this);
        args.Handled = true;
    }

    private void HandlePointerMoved(object? sender, PointerEventArgs args)
    {
        if (_panAnchor is not Point anchor)
        {
            return;
        }

        Point current = args.GetPosition(this);
        Vector delta = current - anchor;
        _panAnchor = current;
        SetState(_state.PanBy(delta.X, delta.Y));
        args.Handled = true;
    }

    private void HandlePointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_panAnchor is null)
        {
            return;
        }

        _panAnchor = null;
        args.Pointer.Capture(null);
        args.Handled = true;
    }

    private void SetState(OblivionDiagramViewportState state)
    {
        _state = state;
        UpdateCamera();
        _stateChanged?.Invoke(state);
    }

    private void UpdateCamera()
    {
        Camera = OblivionDiagramCameraMath.Resolve(
            _state,
            _worldWidth,
            _worldHeight,
            Math.Max(1, Bounds.Width),
            Math.Max(1, Bounds.Height));
        _world.RenderTransform = new MatrixTransform(new Matrix(
            Camera.Scale,
            0,
            0,
            Camera.Scale,
            Camera.OffsetX,
            Camera.OffsetY));
    }
}
