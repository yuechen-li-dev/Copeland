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

    public static void ValidateValues(NativeMsdfQuadSubmission submission)
    {
        ValidateValues(new NativeQuadSubmission(
            submission.Destination,
            submission.Uv,
            submission.AtlasTexture,
            submission.Color));

        NativeMsdfParameters parameters = submission.Msdf;
        if (!float.IsFinite(parameters.PixelRange)
            || !float.IsFinite(parameters.FieldScale)
            || !float.IsFinite(parameters.Threshold))
        {
            throw new ArgumentException("MSDF reconstruction parameters must be finite.", nameof(submission));
        }
        if (parameters.PixelRange <= 0 || parameters.FieldScale <= 0)
        {
            throw new ArgumentException("MSDF pixel range and field scale must be positive.", nameof(submission));
        }
        if (parameters.Threshold < 0 || parameters.Threshold > 1)
        {
            throw new ArgumentException("MSDF threshold must be in [0, 1].", nameof(submission));
        }
    }

    public static void ValidateValues(NativeAnalyticShapeSubmission submission)
    {
        ValidateValues(new NativeQuadSubmission(
            submission.Destination,
            submission.LocalCoordinates,
            default,
            submission.FillColor));
        ValidateTint(submission.BorderColor, nameof(submission));
        if (submission.Destination.Width <= 0 || submission.Destination.Height <= 0)
        {
            throw new ArgumentException("Analytic shape dimensions must be positive.", nameof(submission));
        }
        if (!Enum.IsDefined(submission.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(submission));
        }
        if (submission.Kind == NativeAnalyticShapeKind.Circle && submission.ShapeSize.Width != submission.ShapeSize.Height)
        {
            throw new ArgumentException("Circle destination bounds must be square.", nameof(submission));
        }
        if (!float.IsFinite(submission.Radius) || !float.IsFinite(submission.BorderWidth))
        {
            throw new ArgumentException("Analytic shape parameters must be finite.", nameof(submission));
        }
        if (!float.IsFinite(submission.ShapeSize.Width) || !float.IsFinite(submission.ShapeSize.Height)
            || submission.ShapeSize.Width <= 0 || submission.ShapeSize.Height <= 0)
        {
            throw new ArgumentException("Analytic shape source dimensions must be finite and positive.", nameof(submission));
        }
        float halfMinimum = MathF.Min(submission.ShapeSize.Width, submission.ShapeSize.Height) / 2;
        if (submission.Radius < 0 || submission.Radius > halfMinimum)
        {
            throw new ArgumentOutOfRangeException(nameof(submission), "Analytic shape radius must be in [0, min(width, height) / 2].");
        }
        if (submission.BorderWidth < 0 || submission.BorderWidth > halfMinimum)
        {
            throw new ArgumentOutOfRangeException(nameof(submission), "Analytic border width must be in [0, min(width, height) / 2].");
        }
    }

    public static void ValidateValues(NativeSoftShockwaveSubmission submission)
    {
        ValidateValues(new NativeQuadSubmission(
            submission.Destination,
            submission.LocalCoordinates,
            default,
            submission.Color));
        if (submission.Destination.Width <= 0 || submission.Destination.Height <= 0)
        {
            throw new ArgumentException("Soft shockwave dimensions must be positive.", nameof(submission));
        }
        if (!float.IsFinite(submission.Age)
            || !float.IsFinite(submission.Lifetime)
            || !float.IsFinite(submission.Radius)
            || !float.IsFinite(submission.Thickness)
            || !float.IsFinite(submission.Intensity)
            || !float.IsFinite(submission.Seed))
        {
            throw new ArgumentException("Soft shockwave parameters must be finite.", nameof(submission));
        }
        if (submission.Age < 0 || submission.Lifetime <= 0 || submission.Radius < 0
            || submission.Thickness <= 0 || submission.Intensity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(submission), "Soft shockwave age, lifetime, radius, thickness, and intensity are outside their valid ranges.");
        }
    }

    private static void ValidateTint(Native2DTint tint, string parameterName)
    {
        if (!float.IsFinite(tint.Red) || !float.IsFinite(tint.Green) || !float.IsFinite(tint.Blue) || !float.IsFinite(tint.Alpha)
            || tint.Red < 0 || tint.Red > 1 || tint.Green < 0 || tint.Green > 1
            || tint.Blue < 0 || tint.Blue > 1 || tint.Alpha < 0 || tint.Alpha > 1)
        {
            throw new ArgumentException("Analytic shape colors must be finite and in [0, 1].", parameterName);
        }
    }
}
