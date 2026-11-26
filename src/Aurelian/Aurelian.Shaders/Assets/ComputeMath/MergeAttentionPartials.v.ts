import { KernelParameters } from "./KernelMath";

// Each partial stores the local maximum, denominator and unnormalized value sum.
@compute
@numthreads(128, 1, 1)
function MergeAttentionPartialsMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Scale: WorkgroupArray<f32, 64>,
    @workgroup readwrite LocalMaxima: WorkgroupArray<f32, 64>,
    @workgroup readwrite LocalDenominators: WorkgroupArray<f32, 64>,
    @workgroup readwrite Denominator: WorkgroupArray<f32, 1>,
    @binding(0) readonly Partials: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let stride = p.width + 2;
    let start = group.x * p.inner * stride;
    // Load each tile's softmax state in parallel. Keep the merge in tile order.
    var localMax: f32 = 0.0;
    if (lane < p.inner) {
        let offset = start + lane * stride;
        localMax = Partials[offset];
        LocalMaxima[lane] = localMax;
        LocalDenominators[lane] = Partials[offset + 1];
    }
    WorkgroupBarrier();
    if (lane == 0) {
        var maximum = -3.402823466e38;
        static for (let tile of Indices<64>()) {
            if (tile < p.inner) { maximum = Max(maximum, LocalMaxima[tile]); }
        }
        LocalMaxima[0] = maximum;
    }
    WorkgroupBarrier();
    if (lane < p.inner) {
        Scale[lane] = Exp(localMax - LocalMaxima[0]);
    }
    WorkgroupBarrier();
    if (lane == 0) {
        var denom: f32 = 0.0;
        static for (let tile of Indices<64>()) {
            if (tile < p.inner) { denom = denom + Scale[tile] * LocalDenominators[tile]; }
        }
        Denominator[0] = denom;
    }
    WorkgroupBarrier();
    var values: Array<f32, 4> = [0.0, 0.0, 0.0, 0.0];
    for (let tile of BoundedIndices<64>(p.inner)) {
        let offset = start + tile * stride + 2;
        let scale = Scale[tile];
        static for (let part of Indices<4>()) {
            let channel = part * 128 + lane;
            if (channel < p.width) {
                values[part] = values[part] + scale * Partials[offset + channel];
            }
        }
    }
    static for (let part of Indices<4>()) {
        let channel = part * 128 + lane;
        if (channel < p.width) {
            Output[group.x * p.width + channel] = values[part] / Denominator[0];
        }
    }
}
