namespace Aurelian.Graphics.Vulkan.Native2D;

internal static class Native2DSubmissionValidator
{
    public static void ValidateValues(NativeQuadSubmission submission)
    {
        Native2DRect destination = submission.Destination;
        Native2DUvRect uv = submission.Uv;
        Native2DTint tint = submission.Tint;
        if (!float.IsFinite(destination.X)
            || !float.IsFinite(destination.Y)
            || !float.IsFinite(destination.Width)
            || !float.IsFinite(destination.Height)
            || !float.IsFinite(uv.U0)
            || !float.IsFinite(uv.V0)
            || !float.IsFinite(uv.U1)
            || !float.IsFinite(uv.V1)
            || !float.IsFinite(tint.Red)
            || !float.IsFinite(tint.Green)
            || !float.IsFinite(tint.Blue)
            || !float.IsFinite(tint.Alpha))
        {
            throw new ArgumentException("Quad coordinates, UVs, and tint values must be finite.", nameof(submission));
        }
        if (destination.Width < 0 || destination.Height < 0)
        {
            throw new ArgumentException("Quad destination width and height cannot be negative.", nameof(submission));
        }
        if (uv.U0 > uv.U1 || uv.V0 > uv.V1)
        {
            throw new ArgumentException("Quad UV bounds must be ordered as u0 <= u1 and v0 <= v1.", nameof(submission));
        }
        if (tint.Red < 0 || tint.Red > 1
            || tint.Green < 0 || tint.Green > 1
            || tint.Blue < 0 || tint.Blue > 1
            || tint.Alpha < 0 || tint.Alpha > 1)
        {
            throw new ArgumentException("Quad tint components must be in [0, 1].", nameof(submission));
        }
    }
}
