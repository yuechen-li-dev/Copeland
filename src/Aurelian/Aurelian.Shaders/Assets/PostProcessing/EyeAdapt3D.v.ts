@compute
@numthreads(64, 1, 1)
function EyeAdaptMain(@builtin(groupIndex) lane: u32,
    @workgroup readwrite Bins: WorkgroupArray<f32, 64>,
    @binding(1) readonly Histogram: StorageBuffer<f32>,
    @binding(2) readonly Parameters: StorageBuffer<float4>,
    @binding(3) readwrite Exposure: StorageBuffer<float4>): void {
    var weight: f32 = 0.0;
    for (let block of BoundedIndices<144>(144)) {
        weight = weight + Histogram[block * 64 + lane];
    }
    Bins[lane] = weight;
    WorkgroupBarrier();
    if (lane != 0) { return; }

    var total: f32 = 0.0;
    for (let bin of BoundedIndices<64>(64)) {
        total = total + Bins[bin];
    }
    let timing: float4 = Parameters[0];
    let gain: float4 = Parameters[2];
    let meter: float4 = Parameters[3];
    let low: f32 = total * meter.x;
    let high: f32 = total * meter.y;
    var before: f32 = 0.0;
    var sum: f32 = 0.0;
    for (let bin of BoundedIndices<64>(64)) {
        let after: f32 = before + Bins[bin];
        let retained: f32 = Max(Min(after, high) - Max(before, low), 0.0);
        sum = sum + retained * (Convert<f32>(bin) * (32.0 / 63.0) - 16.0);
        before = after;
    }
    let metered: f32 = sum / Max(high - low, 0.000001);
    let target: f32 = Clamp(gain.x - metered, gain.y, gain.z);
    var adapted: f32 = target;
    var adaptedLuminance: f32 = metered;
    if (timing.w < 0.5) {
        let previous: f32 = Exposure[0].x;
        var tau: f32 = meter.w;
        if (target < previous) { tau = meter.z; }
        adapted = Clamp(previous + (target - previous) * (1.0 - Exp(-timing.z / tau)), gain.y, gain.z);

        // Track retinal adaptation in scene units even when display gain has reached a limit.
        let previousLuminance: f32 = Exposure[1].x;
        var luminanceTau: f32 = meter.w;
        if (metered > previousLuminance) { luminanceTau = meter.z; }
        adaptedLuminance = previousLuminance + (metered - previousLuminance)
            * (1.0 - Exp(-timing.z / luminanceTau));
    }
    Exposure[0] = float4(adapted, target, metered, total);
    Exposure[1] = float4(adaptedLuminance, 0.0, 0.0, 0.0);
}
