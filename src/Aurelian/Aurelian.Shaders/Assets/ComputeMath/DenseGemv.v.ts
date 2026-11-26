import { KernelParameters } from "./KernelMath";

// Four independent output rows per workgroup, each read by 32 adjacent lanes.
@compute
@numthreads(128, 1, 1)
function DenseGemvMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Sums: WorkgroupArray<f32, 128>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Weights: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let row = group.x * 4 + lane / 32;
    let inputLane = lane % 32;
    var sum: f32 = 0.0;
    for (let tile of BoundedIndices<256>(p.width)) {
        let k = tile * 32 + inputLane;
        if (row < p.columns && k < p.inner) {
            sum = sum + Input[k] * Weights[row * p.inner + k];
        }
    }
    Sums[lane] = sum;
    WorkgroupBarrier();
    static for (let stage of Indices<5>()) {
        let offset = 16 >> stage;
        if (inputLane < offset) {
            Sums[lane] = Sums[lane] + Sums[lane + offset];
        }
        WorkgroupBarrier();
    }
    if (row < p.columns && inputLane == 0) {
        Output[row] = Sums[lane];
    }
}
