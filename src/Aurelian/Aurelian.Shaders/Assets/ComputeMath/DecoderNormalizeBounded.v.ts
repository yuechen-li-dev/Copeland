import { KernelParameters } from "./KernelMath";
import { BFloat16 } from "./BFloat16";

@compute
@numthreads(64, 1, 1)
function DecoderNormalizeBoundedMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Squares: WorkgroupArray<f32, 64>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Gamma: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let base = group.x * p.columns;
    var sum: f32 = 0.0;
    for (let tile of BoundedIndices<128>(p.width)) {
        let index = tile * 64 + lane;
        if (index < p.columns) {
            let value = Input[base + index];
            sum = sum + value * value;
        }
    }
    Squares[lane] = sum;
    WorkgroupBarrier();
    static for (let stage of Indices<6>()) {
        let stride = 32 >> stage;
        if (lane < stride) { Squares[lane] = Squares[lane] + Squares[lane + stride]; }
        WorkgroupBarrier();
    }
    let inverse = Pow(Squares[0] / Convert<f32>(p.columns) + p.epsilon, -0.5);
    for (let tile of BoundedIndices<128>(p.width)) {
        let index = tile * 64 + lane;
        if (index < p.columns) {
            var value = Input[base + index] * inverse * Gamma[index];
            if (p.mode == 0) { value = BFloat16(value); }
            Output[base + index] = value;
        }
    }
}
