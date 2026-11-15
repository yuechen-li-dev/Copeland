namespace Aurelian.Graphics.Vulkan.Native2D;

/// <summary>Host wall time, not GPU execution time. Acquire includes image availability wait; copy includes its completion wait.</summary>
public sealed record NativePresentTimings(double AcquireMilliseconds, double CopyMilliseconds, double PresentMilliseconds);
