import { KernelParameters } from "./KernelMath";

// Portable tree reduction instead of assuming a particular subgroup width.
// mode 0 = sum, 1 = maximum, 2 = stable softmax. Width is bounded to 8192.
@compute
@numthreads(64, 1, 1)
function ReductionMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Shared: WorkgroupArray<f32, 64>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let base = group.x * p.columns;
    var maximum: f32 = -3.402823466e38;
    var sum: f32 = 0.0;
    static for (let block of Indices<128>()) {
        let columnIndex = lane + block * 64;
        if (columnIndex < p.columns) {
            let value = Input[base + columnIndex];
            maximum = Max(maximum, value);
            sum = sum + value;
        }
    }
    Shared[lane] = maximum;
    WorkgroupBarrier();
    static for (let stage of Indices<6>()) {
        let stride = 32 >> stage;
        if (lane < stride) {
            Shared[lane] = Max(Shared[lane], Shared[lane + stride]);
        }
        WorkgroupBarrier();
    }
    let rowMaximum = Shared[0];
    WorkgroupBarrier();
    if (p.mode == 2) {
        sum = 0.0;
        static for (let block of Indices<128>()) {
            let columnIndex = lane + block * 64;
            if (columnIndex < p.columns) {
                sum = sum + Exp(Input[base + columnIndex] - rowMaximum);
            }
        }
    }
    Shared[lane] = sum;
    WorkgroupBarrier();
    static for (let stage of Indices<6>()) {
        let stride = 32 >> stage;
        if (lane < stride) {
            Shared[lane] = Shared[lane] + Shared[lane + stride];
        }
        WorkgroupBarrier();
    }
    if (p.mode == 2) {
        let denominator = Shared[0];
        static for (let block of Indices<128>()) {
            let columnIndex = lane + block * 64;
            if (columnIndex < p.columns) {
                Output[base + columnIndex] = Exp(Input[base + columnIndex] - rowMaximum) / denominator;
            }
        }
    } else if (lane == 0) {
        if (p.mode == 1) {
            Output[group.x] = rowMaximum;
        } else {
            Output[group.x] = Shared[0];
        }
    }
}
