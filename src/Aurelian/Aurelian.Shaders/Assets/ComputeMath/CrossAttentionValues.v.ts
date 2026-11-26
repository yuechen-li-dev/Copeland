import { KernelParameters } from "./KernelMath";

// Lanes read contiguous channels and reduce eight key partitions per channel.
@compute
@numthreads(128, 1, 1)
function CrossAttentionValuesMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Sums: WorkgroupArray<f32, 128>,
    @binding(0) readonly Probabilities: StorageBuffer<f32>,
    @binding(1) readonly Values: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let channel = group.y * p.width + group.x * 16 + lane % 16;
    let rowWidth = p.height * p.width;
    let partition = lane / 16;
    var sum: f32 = 0.0;
    for (let tile of BoundedIndices<256>(p.inner)) {
        let key = tile * 8 + partition;
        if (key < p.columns) {
            sum = sum + Probabilities[group.y * p.columns + key] * Values[key * rowWidth + channel];
        }
    }
    Sums[lane] = sum;
    WorkgroupBarrier();
    static for (let stage of Indices<3>()) {
        let offset = 4 >> stage;
        if (partition < offset) {
            Sums[lane] = Sums[lane] + Sums[lane + offset * 16];
        }
        WorkgroupBarrier();
    }
    if (partition == 0) {
        Output[channel] = Sums[lane];
    }
}
