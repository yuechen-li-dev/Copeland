using System.Numerics;

namespace Aurelian.Effects2D;

public readonly record struct EffectCameraTransform(
    Vector2 CameraWorldTopLeft,
    Vector2 ViewportPixelOrigin,
    float PixelsPerWorldUnit,
    float Zoom)
{
    public Vector2 WorldToScreen(Vector2 worldPosition)
    {
        if (!float.IsFinite(PixelsPerWorldUnit) || PixelsPerWorldUnit <= 0
            || !float.IsFinite(Zoom) || Zoom <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PixelsPerWorldUnit), "Camera scale and zoom must be finite and positive.");
        }
        return ViewportPixelOrigin + ((worldPosition - CameraWorldTopLeft) * PixelsPerWorldUnit * Zoom);
    }

    public Vector2 Project(Vector2 position, EffectCoordinateSpace space)
        => space == EffectCoordinateSpace.World ? WorldToScreen(position) : position;
}
