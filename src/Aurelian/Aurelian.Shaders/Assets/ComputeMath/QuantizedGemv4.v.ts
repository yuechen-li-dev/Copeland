import { KernelParameters } from "./KernelMath";

@compute
@numthreads(128, 1, 1)
function QuantizedGemv4Main(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Sums: WorkgroupArray<f32, 128>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Weights: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let row = group.x * 4 + lane / 32;
    let channel = lane % 32;
    var sum: f32 = 0.0;
    for (let tile of BoundedIndices<128>(p.width)) {
        if (row < p.columns) {
            let first = (tile * 32 + channel) * 8;
            if (first < p.inner) {
                let address = (row * p.inner + first) / 32 * 5;
                let word = Weights[address + 1 + channel % 4];
                var blockSum: f32 = 0.0;
                static for (let component of Indices<8>()) {
                    let nibble = (word >> (component * 4)) & 15;
                    blockSum = blockSum + Input[first + component] * (Convert<f32>(nibble) - 8.0);
                }
                sum = sum + blockSum * Bitcast<f32>(Weights[address]);
            }
        }
    }
    Sums[lane] = sum;
    WorkgroupBarrier();
    static for (let stage of Indices<5>()) {
        let stride = 16 >> stage;
        if (channel < stride) { Sums[lane] = Sums[lane] + Sums[lane + stride]; }
        WorkgroupBarrier();
    }
    if (row < p.columns && channel == 0) { Output[row] = Sums[lane]; }
}
