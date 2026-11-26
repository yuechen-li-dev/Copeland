import { KernelParameters } from "./KernelMath";
import { GemvLanes, GemvStages } from "./GemvLanes32";

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
    let row = group.x * (128 / GemvLanes) + lane / GemvLanes;
    let channel = lane % GemvLanes;
    var sum: f32 = 0.0;
    for (let tile of BoundedIndices<512>(p.width)) {
        if (row < p.columns) {
            let first = (tile * GemvLanes + channel) * 8;
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
    for (let stage of BoundedIndices<5>(GemvStages)) {
        let stride = (GemvLanes / 2) >> stage;
        if (channel < stride) { Sums[lane] = Sums[lane] + Sums[lane + stride]; }
        WorkgroupBarrier();
    }
    if (row < p.columns && channel == 0) { Output[row] = Sums[lane]; }
}
