// One sample per lane in a fixed 128x72 meter. Workgroup-local bins avoid
// contended global atomics and keep cost independent of display resolution.
@compute
@numthreads(64, 1, 1)
function EyeHistogramMain(@builtin(dispatchThreadId) thread: uint3, @builtin(groupIndex) lane: u32,
    @builtin(groupId) group: uint3,
    @workgroup readwrite SampleBins: WorkgroupArray<f32, 64>,
    @workgroup readwrite SampleWeights: WorkgroupArray<f32, 64>,
    @binding(0) readonly Scene: Texture2D<float4>,
    @binding(1) readwrite Histogram: StorageBuffer<f32>,
    @binding(2) readonly Parameters: StorageBuffer<float4>): void {
    let extent: float4 = Parameters[0];
    let mask: float4 = Parameters[1];
    let u: f32 = (Convert<f32>(thread.x % 128) + 0.5) / 128.0;
    let v: f32 = (Convert<f32>(thread.x / 128) + 0.5) / 72.0;
    let coordinate: uint2 = uint2(U32(u * extent.x), U32(v * extent.y));
    let color: float4 = Load(Scene, coordinate);
    let luma: f32 = 0.2126 * color.x + 0.7152 * color.y + 0.0722 * color.z;
    var bin: f32 = 0.0;
    if (luma > 0.0000152587890625) {
        bin = Clamp((Log(Min(luma, 65536.0)) * 1.44269504089 + 16.0) * (63.0 / 32.0), 0.0, 63.0);
    }
    let x: f32 = (u - mask.x) / mask.z;
    let y: f32 = (v - mask.y) / mask.z;
    let spot: f32 = Max(1.0 - x * x - y * y, 0.0);
    let weight: f32 = mask.w + (1.0 - mask.w) * spot * spot;
    SampleBins[lane] = bin;
    SampleWeights[lane] = weight;
    WorkgroupBarrier();

    var count: f32 = 0.0;
    for (let sample of BoundedIndices<64>(64)) {
        count = count + SampleWeights[sample] * Max(1.0 - Abs(SampleBins[sample] - Convert<f32>(lane)), 0.0);
    }
    Histogram[group.x * 64 + lane] = count;
}
