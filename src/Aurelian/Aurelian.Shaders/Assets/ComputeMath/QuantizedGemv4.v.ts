import { KernelParameters } from "./KernelMath";
import { GemvLanes, GemvStages, Q4Rows, Q4ScaleWords } from "./GemvLanes32";
import { Q4BlockAddress, Q4Scale } from "./PackedQ4Address";

function DotEight(readonly input: StorageBuffer<f32>, readonly weights: StorageBuffer<u32>,
    row: u32, first: u32, width: u32): f32 {
    let address = Q4BlockAddress(row, first, width, Q4Rows, Q4ScaleWords);
    let rowInBlock = row % Q4Rows;
    let word = weights[address + Q4ScaleWords + (first % 32 / 8) * Q4Rows + rowInBlock];
    var blockSum: f32 = 0.0;
    static for (let component of Indices<8>()) {
        let nibble = (word >> (component * 4)) & 15;
        blockSum = blockSum + input[first + component] * (Convert<f32>(nibble) - 8.0);
    }
    return blockSum * Q4Scale(weights, address, rowInBlock, Q4Rows, Q4ScaleWords);
}

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
    let rowsPerGroup = 128 / GemvLanes;
    var row = group.x * rowsPerGroup + lane / GemvLanes;
    var channel = lane % GemvLanes;
    if (Q4Rows != 1) {
        row = group.x * rowsPerGroup + lane % rowsPerGroup;
        channel = lane / rowsPerGroup;
    }
    var sum: f32 = 0.0;
    var nextSum: f32 = 0.0;
    for (let tile of BoundedIndices<512>(p.width)) {
        if (Q4ScaleWords == 4) {
            // Two independent K partials let the compiler interleave loads and arithmetic.
            let first = (tile * GemvLanes * 2 + channel) * 8;
            sum = sum + DotEight(Input, Weights, p.height + row, first, p.inner);
            nextSum = nextSum + DotEight(Input, Weights, p.height + row, first + GemvLanes * 8, p.inner);
        } else {
            let first = (tile * GemvLanes + channel) * 8;
            if (row < p.columns && first < p.inner) {
                sum = sum + DotEight(Input, Weights, p.height + row, first, p.inner);
            }
        }
    }
    if (Q4ScaleWords == 4) {
        sum = sum + nextSum;
    }
    Sums[lane] = sum;
    WorkgroupBarrier();
    for (let stage of BoundedIndices<5>(GemvStages)) {
        let stride = (GemvLanes / 2) >> stage;
        let laneOffset = stride * Q4Rows;
        if (channel < stride) { Sums[lane] = Sums[lane] + Sums[lane + laneOffset]; }
        WorkgroupBarrier();
    }
    if (row < p.columns && channel == 0) { Output[row] = Sums[lane]; }
}
