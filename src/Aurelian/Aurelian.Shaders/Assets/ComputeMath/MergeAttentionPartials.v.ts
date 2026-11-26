import { KernelParameters } from "./KernelMath";

// Each partial stores the local maximum, denominator and unnormalized value sum.
@compute
@numthreads(128, 1, 1)
function MergeAttentionPartialsMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Scale: WorkgroupArray<f32, 32>,
    @workgroup readwrite Denominator: WorkgroupArray<f32, 1>,
    @binding(0) readonly Partials: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let stride = p.width + 2;
    let start = group.x * p.inner * stride;
    if (lane == 0) {
        var maximum = -3.402823466e38;
        static for (let tile of Indices<32>()) {
            if (tile < p.inner) {
                maximum = Max(maximum, Partials[start + tile * stride]);
            }
        }
        var denom: f32 = 0.0;
        static for (let tile of Indices<32>()) {
            if (tile < p.inner) {
                let offset = start + tile * stride;
                let scale = Exp(Partials[offset] - maximum);
                Scale[tile] = scale;
                denom = denom + scale * Partials[offset + 1];
            }
        }
        Denominator[0] = denom;
    }
    WorkgroupBarrier();
    var values: Array<f32, 4> = [0.0, 0.0, 0.0, 0.0];
    for (let tile of BoundedIndices<32>(p.inner)) {
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
