using System.Diagnostics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Inference;

public sealed record QuantizedLayerTimings(long Calls, long Dispatches, long GpuCacheHits, long HostCacheHits,
    double ReadMilliseconds, double AllocationMilliseconds, double UploadMilliseconds,
    double ExecuteMilliseconds, double? GpuMilliseconds);

/// <summary>Explicit first-use residency budget. Cached matrices remain resident;
/// uncached matrices use one temporary packed allocation per operation. An optional
/// separate host budget retains private, validated packed slices until disposal.
/// Like its compute owner, this adapter is used sequentially.</summary>
public sealed class VulkanQuantizedLayers : IDisposable
{
    private readonly VulkanComputeMath compute;
    private readonly QuantizedCheckpoint weights;
    private readonly long residentBudget;
    private readonly long hostCacheBudget;
    private readonly Dictionary<string, GpuTensor> resident = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Name, int First, int Count), byte[]> hostCache = [];
    private bool disposed;
    private byte[] uploadScratch = [];
    private long calls;
    private long dispatches;
    private long cacheHits;
    private long hostCacheHits;
    private double readMilliseconds;
    private double allocationMilliseconds;
    private double uploadMilliseconds;
    private double executeMilliseconds;
    private double gpuMilliseconds;
    private bool gpuTimingAvailable = true;
    public long ResidentBytes { get; private set; }
    public long UploadedBytes { get; private set; }
    public long HostCacheBytes { get; private set; }
    public QuantizedLayerTimings Timings => new(calls, dispatches, cacheHits, hostCacheHits,
        readMilliseconds, allocationMilliseconds, uploadMilliseconds, executeMilliseconds,
        gpuTimingAvailable ? gpuMilliseconds : null);
    public bool UseChunkedReference { get; init; }
    public bool UseSeparateUploads { get; init; }
    public int HostScratchBytes => uploadScratch.Length;

    public VulkanQuantizedLayers(VulkanComputeMath compute, QuantizedCheckpoint weights,
        long residentBudget = 0, long hostCacheBudget = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(residentBudget);
        ArgumentOutOfRangeException.ThrowIfNegative(hostCacheBudget);
        this.compute = compute;
        this.weights = weights;
        this.residentBudget = residentBudget;
        this.hostCacheBudget = hostCacheBudget;
    }

    public GpuTensor Linear(GpuTensor input, string name, int firstRow = 0, int? rowCount = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var matrix = weights.Matrices[name];
        int rows = rowCount ?? matrix.Rows;
        if (input.Columns != matrix.Columns || rows is < 1 or > 32768 || firstRow < 0 || (long)firstRow + rows > matrix.Rows)
        {
            throw new InvalidDataException("AUR-Q4-005: Input/output shape differs from authored matrix slice.");
        }
        bool whole = firstRow == 0 && rows == matrix.Rows;
        GpuTensor? temporary = null;
        GpuTensor packed;
        ReadOnlyMemory<byte> pendingUpload = default;
        bool cacheOnGpu = false;
        if (!whole || !resident.TryGetValue(name, out packed!))
        {
            long start = Stopwatch.GetTimestamp();
            int byteCount = checked(rows * (matrix.Columns / 32) * 20);
            if (byteCount > 134217728 || byteCount / sizeof(uint) > compute.MaximumTensorElements)
            {
                throw new InvalidDataException("AUR-Q4-005: Packed matrix slice exceeds the checkpoint/compute element budget.");
            }
            cacheOnGpu = whole && byteCount <= residentBudget - ResidentBytes;
            pendingUpload = ReadPacked(name, firstRow, rows, byteCount, cacheOnGpu);
            readMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            packed = compute.CreateSparseTensor(rows * BlockQ4.WordsPerBlock,
                matrix.Columns / BlockQ4.ValuesPerBlock, GpuTensorFormat.UInt32);
            allocationMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            temporary = packed;
        }
        else
        {
            cacheHits++;
        }
        try
        {
            bool uploading = !pendingUpload.IsEmpty;
            if (uploading && UseSeparateUploads)
            {
                long start = Stopwatch.GetTimestamp();
                compute.UploadUInt32Bytes(packed, pendingUpload);
                uploadMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                pendingUpload = default;
            }
            var output = compute.CreateTensor(input.Rows, rows);
            try
            {
                var batch = compute.CreateBatch();
                batch.QuantizedLinear(input, packed, output, roundBFloat16: true,
                    chunkedReference: UseChunkedReference);
                long start = Stopwatch.GetTimestamp();
                ComputeMathResult result = pendingUpload.IsEmpty
                    ? compute.Execute(batch)
                    : compute.ExecuteWithUInt32Upload(batch, packed, pendingUpload);
                executeMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (uploading)
                {
                    UploadedBytes += checked((long)packed.Buffer.SizeBytes);
                    if (cacheOnGpu)
                    {
                        resident.Add(name, packed);
                        ResidentBytes += (long)packed.Buffer.SizeBytes;
                        temporary = null;
                    }
                }
                calls++;
                dispatches += result.Dispatches;
                if (result.GpuMilliseconds is { } milliseconds)
                {
                    gpuMilliseconds += milliseconds;
                }
                else
                {
                    gpuTimingAvailable = false;
                }
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
        finally
        {
            temporary?.Dispose();
        }
    }

    private ReadOnlyMemory<byte> ReadPacked(string name, int first, int count, int byteCount, bool cacheOnGpu)
    {
        var key = (name, first, count);
        if (hostCache.TryGetValue(key, out byte[]? cached))
        {
            hostCacheHits++;
            return cached;
        }
        // Cache only private, validated bytes. GPU-resident matrices do not need a second cache.
        bool retain = !cacheOnGpu && byteCount <= hostCacheBudget - HostCacheBytes;
        byte[] destination;
        if (retain)
        {
            destination = GC.AllocateUninitializedArray<byte>(byteCount);
        }
        else
        {
            if (uploadScratch.Length < byteCount)
            {
                uploadScratch = GC.AllocateUninitializedArray<byte>(byteCount);
            }
            destination = uploadScratch;
        }
        weights.ReadRows(name, first, count,
            MemoryMarshal.Cast<byte, uint>(destination.AsSpan(0, byteCount)));
        if (retain)
        {
            hostCache.Add(key, destination);
            HostCacheBytes += byteCount;
        }
        return destination.AsMemory(0, byteCount);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        foreach (var tensor in resident.Values) { tensor.Dispose(); }
        resident.Clear();
        hostCache.Clear();
        HostCacheBytes = 0;
        uploadScratch = [];
        ResidentBytes = 0;
    }
}
