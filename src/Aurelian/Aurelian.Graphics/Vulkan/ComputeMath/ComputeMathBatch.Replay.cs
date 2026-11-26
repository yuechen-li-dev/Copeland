namespace Aurelian.Graphics.Vulkan.ComputeMath;

internal enum TokenReplayUpdateKind { Offset, SplitAttention, MergeAttention }

internal readonly record struct TokenReplayUpdate(TokenReplayUpdateKind Kind, int OffsetBias = 0,
    int Window = 0, int TileKeys = 0)
{
    public void Apply(int position, ref KernelParameters parameters, ref uint groupsY)
    {
        if (Kind == TokenReplayUpdateKind.Offset)
        {
            parameters.Offset = (uint)(position + OffsetBias);
            return;
        }
        int keyCount = Math.Min(position + 1, Window);
        uint tiles = (uint)((keyCount + TileKeys - 1) / TileKeys);
        if (Kind == TokenReplayUpdateKind.SplitAttention)
        {
            parameters.Offset = (uint)(position + 1 - keyCount);
            parameters.Columns = (uint)keyCount;
            parameters.Channels = tiles;
            groupsY = tiles;
        }
        else
        {
            parameters.Inner = tiles;
        }
    }
}

public sealed partial class ComputeMathBatch
{
    private const int MaximumTokenReplayDispatches = 1024;
    internal int TokenReplayCapacity { get; init; }
    internal int TokenReplayPosition { get; init; }

    private void TrackTokenOffset(int offset, int capacity, int rows)
    {
        if (TokenReplayCapacity == 0) { return; }
        int bias = offset - TokenReplayPosition;
        Require(rows == 1 && bias >= 0 && (long)bias + TokenReplayCapacity <= capacity,
            "A replayed positional operation must fit a single token across the replay range.");
        Dispatches[^1] = Dispatches[^1] with { TokenUpdate = new(TokenReplayUpdateKind.Offset, bias) };
    }

    private void TrackTokenAttention(int position, int capacity, int heads, int window, int tileKeys, int partialRows)
    {
        if (TokenReplayCapacity == 0) { return; }
        Require(position == TokenReplayPosition && TokenReplayCapacity <= capacity
            && partialRows >= heads * Groups(Math.Min(TokenReplayCapacity, window), tileKeys),
            "Replayed attention needs cache and partials for the maximum replay position.");
        Dispatches[^2] = Dispatches[^2] with
        {
            TokenUpdate = new(TokenReplayUpdateKind.SplitAttention, Window: window, TileKeys: tileKeys),
        };
        Dispatches[^1] = Dispatches[^1] with
        {
            TokenUpdate = new(TokenReplayUpdateKind.MergeAttention, Window: window, TileKeys: tileKeys),
        };
    }
}
