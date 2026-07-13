namespace Aurelian.Core.Engine.Frames;

/// <summary>
/// Engine-owned host facts needed by a frame. Platform callbacks and frontend
/// objects are intentionally translated before this boundary.
/// </summary>
public sealed record AurelianHostLifecycleInput(
    AurelianHostExtent? HostExtent,
    bool CloseRequested)
{
    public static AurelianHostLifecycleInput None { get; } = new(null, false);
}

public readonly record struct AurelianHostExtent
{
    public AurelianHostExtent(uint width, uint height)
    {
        if (width == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Host width must be greater than zero.");
        }

        if (height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Host height must be greater than zero.");
        }

        Width = width;
        Height = height;
    }

    public uint Width { get; }

    public uint Height { get; }
}
