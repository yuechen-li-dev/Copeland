namespace Machina.Layout.Frames;

public abstract record GridTrack;

public sealed record FixedGridTrack(double Size) : GridTrack;

public sealed record FillGridTrack(double Weight = 1) : GridTrack;
