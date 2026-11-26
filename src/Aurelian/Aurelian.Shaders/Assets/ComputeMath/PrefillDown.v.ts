import { KernelParameters } from "./KernelMath";

// Eight prompt rows by four output channels. Lanes read adjacent K values
// from the native [output,input] weights, then reduce each dot product.
@compute
@numthreads(32, 8, 1)
function PrefillDownMain(@builtin(groupId) group: uint3,
    @builtin(groupThreadId) local: uint3,
    @workgroup readwrite Partial: WorkgroupArray<f32, 1024>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Weights: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let row = group.x * 8 + local.y;
    let col = group.y * 4;
    var sums: Array<f32, 4> = [0.0, 0.0, 0.0, 0.0];
    for (let block of BoundedIndices<96>(96)) {
        let k = block * 32 + local.x;
        let x = Input[row * 3072 + k];
        static for (let j of Indices<4>()) {
            sums[j] = sums[j] + x * Weights[(col + j) * 3072 + k];
        }
    }
    static for (let j of Indices<4>()) {
        Partial[(local.y * 4 + j) * 32 + local.x] = sums[j];
    }
    WorkgroupBarrier();
    static for (let stage of Indices<5>()) {
        let stride = 16 >> stage;
        if (local.x < stride) {
            static for (let j of Indices<4>()) {
                let cell = (local.y * 4 + j) * 32 + local.x;
                Partial[cell] = Partial[cell] + Partial[cell + stride];
            }
        }
        WorkgroupBarrier();
    }
    if (local.x < 4) {
        Output[row * Parameters[0].columns + col + local.x] = Partial[(local.y * 4 + local.x) * 32];
    }
}
