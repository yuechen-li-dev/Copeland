# Span allocation

`Copeland.SpanAllocation` resolves ordered typed requests over one finite non-negative integer extent. It has no dependency on sprites, textures, UI, Machina, Aurelian, or memory-management concepts.

The M15 surface is deliberately small:

```csharp
SpanAllocationRequest<T>.Fixed(payload, length)
SpanAllocationRequest<T>.Flex(payload, minimumLength, weight)
SpanAllocator.Resolve(extent, requests)
```

Every request first reserves its fixed length or flex minimum. If demand equals extent, the result is `Exact`. If space remains and flex requests exist, integer weighted distribution produces `SurplusDistributed`; fractional remainders are awarded deterministically by remainder then request order. With no flex request, remaining space is `SurplusUnused`.

If minimum demand exceeds extent, the result is `Underflow`. Placements are clipped in request order, remain contiguous, and include `COPE-SPAN-ALLOC-0100`. Invalid extent, length, kind, or weight returns `Rejected` and no placements.

The result reports extent, minimum demand, used, unused, deficit, status, placements, and diagnostics. Each placement retains typed payload `T`, request index, offset, length, and fixed/flex kind.

Allocation says only where and how long. A sprite adapter may later realize the span by stretching, tiling, or cropping; those sampling policies are not allocator inputs.

Not implemented: alignment, preferred/maximum/intrinsic sizing, optional/priority requests, holes, compaction, pinning, memory ownership, fractional extents, or two-dimensional layout.
