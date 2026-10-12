import { KernelParameters } from "./KernelMath";

// Q4B32 is decoded in registers. No expanded FP32 matrix is allocated.
@compute
@numthreads(64, 1, 1)
function QuantizedLinearMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Sums: WorkgroupArray<f32, 64>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Weights: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let row = group.y;
    let columnIndex = group.x;
    var sum: f32 = 0.0;
    static for (let tile of Indices<16>()) {
        let k = p.offset + lane + tile * 64;
        if (k < p.inner) {
            let element = columnIndex * p.inner + k;
            let block = element / 32 * 5;
            let scale = Bitcast<f32>(Weights[block]);
            let nibble = Weights[block + 1 + element % 32 / 8] >> (element % 8 * 4) & 15;
            sum = sum + Input[row * p.inner + k] * scale * (Convert<f32>(nibble) - 8.0);
        }
    }
    Sums[lane] = sum;
    WorkgroupBarrier();
    static for (let stage of Indices<6>()) {
        let stride = 32 >> stage;
        if (lane < stride) {
            Sums[lane] = Sums[lane] + Sums[lane + stride];
        }
        WorkgroupBarrier();
    }
    if (lane == 0) {
        let index = row * p.columns + columnIndex;
        if (p.offset == 0) { Output[index] = Sums[0]; }
        else { Output[index] = Output[index] + Sums[0]; }
    }
}
