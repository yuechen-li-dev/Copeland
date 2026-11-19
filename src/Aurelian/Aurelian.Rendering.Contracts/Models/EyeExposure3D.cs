using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

/// <summary>Scene-relative gain in stops, with authored response times in seconds.</summary>
public sealed record EyeExposure3D
{
    public float CompensationEv { get; init; }
    public float MinimumEv { get; init; } = -6;
    public float MaximumEv { get; init; } = 6;
    public float BrightTimeConstant { get; init; } = .18f;
    public float DarkTimeConstant { get; init; } = 1.4f;
    public float LowPercentile { get; init; } = .1f;
    public float HighPercentile { get; init; } = .9f;
    public Vector2 MeterCenter { get; init; } = new(.5f);
    public float MeterRadius { get; init; } = .5f;
    public float PeripheralWeight { get; init; } = .1f;

    internal void Validate()
    {
        if (!InRange(CompensationEv, -12, 12)
            || !InRange(MinimumEv, -12, 12) || !InRange(MaximumEv, MinimumEv, 12)
            || !InRange(BrightTimeConstant, .01f, 120) || !InRange(DarkTimeConstant, .01f, 120)
            || !InRange(LowPercentile, 0, 1) || !InRange(HighPercentile, LowPercentile + .01f, 1)
            || !InRange(MeterCenter.X, 0, 1) || !InRange(MeterCenter.Y, 0, 1)
            || !InRange(MeterRadius, .05f, 2) || !InRange(PeripheralWeight, 0, 1))
        {
            throw new ArgumentOutOfRangeException(nameof(EyeExposure3D), "Exposure stops, time constants and metering must be finite and within range.");
        }
    }

    private static bool InRange(float value, float minimum, float maximum) =>
        float.IsFinite(value) && value >= minimum && value <= maximum;
}
