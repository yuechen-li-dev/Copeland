namespace Copeland.SpanAllocation;

public enum SpanAllocationKind
{
    Fixed,
    Flex,
}

public enum SpanAllocationStatus
{
    Exact,
    SurplusDistributed,
    SurplusUnused,
    Underflow,
    Rejected,
}

public sealed record SpanAllocationRequest<T>
{
    private SpanAllocationRequest(
        T payload,
        SpanAllocationKind kind,
        int minimumLength,
        int weight)
    {
        Payload = payload;
        Kind = kind;
        MinimumLength = minimumLength;
        Weight = weight;
    }

    public T Payload { get; }

    public SpanAllocationKind Kind { get; }

    public int MinimumLength { get; }

    public int Weight { get; }

    public static SpanAllocationRequest<T> Fixed(T payload, int length)
    {
        return new SpanAllocationRequest<T>(payload, SpanAllocationKind.Fixed, length, 0);
    }

    public static SpanAllocationRequest<T> Flex(T payload, int minimumLength, int weight = 1)
    {
        return new SpanAllocationRequest<T>(payload, SpanAllocationKind.Flex, minimumLength, weight);
    }
}

public sealed record SpanPlacement<T>(
    T Payload,
    int RequestIndex,
    int Offset,
    int Length,
    SpanAllocationKind Kind);

public sealed record SpanAllocationDiagnostic(
    string Code,
    string Message,
    int? RequestIndex = null);

public sealed record SpanAllocationResult<T>(
    int Extent,
    int MinimumDemand,
    int UsedLength,
    int UnusedLength,
    int DeficitLength,
    SpanAllocationStatus Status,
    IReadOnlyList<SpanPlacement<T>> Placements,
    IReadOnlyList<SpanAllocationDiagnostic> Diagnostics)
{
    public bool Success => Status is not SpanAllocationStatus.Rejected;
}

/// <summary>
/// Deterministically resolves ordered fixed and minimum-weighted-flex requests
/// over one finite integer extent. The allocator knows nothing about pixels,
/// sprites, memory, or sampling.
/// </summary>
public static class SpanAllocator
{
    public static SpanAllocationResult<T> Resolve<T>(
        int extent,
        IReadOnlyList<SpanAllocationRequest<T>> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var diagnostics = new List<SpanAllocationDiagnostic>();
        if (extent < 0)
        {
            diagnostics.Add(new SpanAllocationDiagnostic(
                "COPE-SPAN-ALLOC-0001",
                "Extent must be non-negative."));
        }

        long minimumDemandLong = 0;
        long totalWeightLong = 0;
        for (int index = 0; index < requests.Count; index++)
        {
            SpanAllocationRequest<T>? request = requests[index];
            if (request is null)
            {
                diagnostics.Add(new SpanAllocationDiagnostic(
                    "COPE-SPAN-ALLOC-0002",
                    "Allocation request must not be null.",
                    index));
                continue;
            }

            if (request.MinimumLength < 0)
            {
                diagnostics.Add(new SpanAllocationDiagnostic(
                    "COPE-SPAN-ALLOC-0003",
                    "Minimum length must be non-negative.",
                    index));
            }

            if (request.Kind == SpanAllocationKind.Fixed && request.Weight != 0)
            {
                diagnostics.Add(new SpanAllocationDiagnostic(
                    "COPE-SPAN-ALLOC-0004",
                    "A fixed request cannot carry flex weight.",
                    index));
            }

            if (request.Kind == SpanAllocationKind.Flex && request.Weight <= 0)
            {
                diagnostics.Add(new SpanAllocationDiagnostic(
                    "COPE-SPAN-ALLOC-0005",
                    "A flex request requires a positive integer weight.",
                    index));
            }

            if (!Enum.IsDefined(request.Kind))
            {
                diagnostics.Add(new SpanAllocationDiagnostic(
                    "COPE-SPAN-ALLOC-0006",
                    "Allocation kind is invalid.",
                    index));
            }

            minimumDemandLong += Math.Max(request.MinimumLength, 0);
            if (request.Kind == SpanAllocationKind.Flex)
            {
                totalWeightLong += Math.Max(request.Weight, 0);
            }
        }

        if (minimumDemandLong > int.MaxValue || totalWeightLong > int.MaxValue)
        {
            diagnostics.Add(new SpanAllocationDiagnostic(
                "COPE-SPAN-ALLOC-0007",
                "Allocation demand exceeds the supported 32-bit extent domain."));
        }

        if (diagnostics.Count > 0)
        {
            return new SpanAllocationResult<T>(
                extent,
                minimumDemandLong > int.MaxValue ? int.MaxValue : (int)minimumDemandLong,
                0,
                Math.Max(extent, 0),
                0,
                SpanAllocationStatus.Rejected,
                [],
                diagnostics);
        }

        int minimumDemand = (int)minimumDemandLong;
        var lengths = requests.Select(request => request.MinimumLength).ToArray();
        SpanAllocationStatus status;
        int deficit = 0;

        if (minimumDemand > extent)
        {
            status = SpanAllocationStatus.Underflow;
            deficit = minimumDemand - extent;
            diagnostics.Add(new SpanAllocationDiagnostic(
                "COPE-SPAN-ALLOC-0100",
                $"Minimum demand {minimumDemand} exceeds extent {extent} by {deficit}; placements are deterministically clipped in request order."));
        }
        else
        {
            int surplus = extent - minimumDemand;
            int totalWeight = (int)totalWeightLong;
            if (surplus == 0)
            {
                status = SpanAllocationStatus.Exact;
            }
            else if (totalWeight == 0)
            {
                status = SpanAllocationStatus.SurplusUnused;
            }
            else
            {
                status = SpanAllocationStatus.SurplusDistributed;
                DistributeSurplus(requests, lengths, surplus, totalWeight);
            }
        }

        var placements = new List<SpanPlacement<T>>(requests.Count);
        int offset = 0;
        for (int index = 0; index < requests.Count; index++)
        {
            SpanAllocationRequest<T> request = requests[index];
            int remaining = Math.Max(0, extent - offset);
            int length = Math.Min(lengths[index], remaining);
            placements.Add(new SpanPlacement<T>(
                request.Payload,
                index,
                offset,
                length,
                request.Kind));
            offset += length;
        }

        return new SpanAllocationResult<T>(
            extent,
            minimumDemand,
            offset,
            Math.Max(0, extent - offset),
            deficit,
            status,
            placements,
            diagnostics);
    }

    private static void DistributeSurplus<T>(
        IReadOnlyList<SpanAllocationRequest<T>> requests,
        int[] lengths,
        int surplus,
        int totalWeight)
    {
        int distributed = 0;
        var remainders = new List<(int Index, long Remainder)>();
        for (int index = 0; index < requests.Count; index++)
        {
            SpanAllocationRequest<T> request = requests[index];
            if (request.Kind != SpanAllocationKind.Flex)
            {
                continue;
            }

            long weighted = (long)surplus * request.Weight;
            int share = (int)(weighted / totalWeight);
            lengths[index] += share;
            distributed += share;
            remainders.Add((index, weighted % totalWeight));
        }

        int remainderUnits = surplus - distributed;
        foreach ((int index, _) in remainders
                     .OrderByDescending(item => item.Remainder)
                     .ThenBy(item => item.Index)
                     .Take(remainderUnits))
        {
            lengths[index] += 1;
        }
    }
}
