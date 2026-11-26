import { Gelu, KernelParameters } from "./KernelMath";
import { Q4BlockAddress, Q4UnsignedDotWord, Q4Scale } from "./PackedQ4Address";

function AccumulateGateUp(readonly input: StorageBuffer<float4>, readonly weights: StorageBuffer<u32>,
    row: u32, first: u32, width: u32, upOffset: u32): float2 {
    let address = Q4BlockAddress(row, first, width, 8, 4);
    let rowInBlock = row % 8;
    let word = address + 4 + first % 32 / 8 * 8 + rowInBlock;
    let left = input[first / 4];
    let right = input[first / 4 + 1];
    // The Q4 zero point is shared by both projections of these eight activations.
    let correction = Dot(left + right, float4(8.0, 8.0, 8.0, 8.0));
    let gate = (Q4UnsignedDotWord(weights[word], left, right) - correction) * Q4Scale(weights, address, rowInBlock, 8, 4);
    let up = (Q4UnsignedDotWord(weights[word + upOffset], left, right) - correction)
        * Q4Scale(weights, address + upOffset, rowInBlock, 8, 4);
    return float2(gate, up);
}

@compute
@numthreads(128, 1, 1)
function QuantizedQ4GateUpMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite GateSums: WorkgroupArray<f32, 128>,
    @workgroup readwrite UpSums: WorkgroupArray<f32, 128>,
    @binding(0) readonly Input: StorageBuffer<float4>,
    @binding(1) readonly Weights: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let row = group.x * 8 + lane % 8;
    let channel = lane / 8;
    var sums = float2(0.0, 0.0);
    var next = float2(0.0, 0.0);
    for (let tile of BoundedIndices<128>(p.width)) {
        let first = tile * 256 + channel * 8;
        sums = sums + AccumulateGateUp(Input, Weights, row, first, p.inner, p.offset);
        next = next + AccumulateGateUp(Input, Weights, row, first + 128, p.inner, p.offset);
    }
    GateSums[lane] = sums.x + next.x;
    UpSums[lane] = sums.y + next.y;
    WorkgroupBarrier();
    static for (let stage of Indices<4>()) {
        let stride = 8 >> stage;
        if (channel < stride) {
            GateSums[lane] = GateSums[lane] + GateSums[lane + stride * 8];
            UpSums[lane] = UpSums[lane] + UpSums[lane + stride * 8];
        }
        WorkgroupBarrier();
    }
    if (channel == 0) {
        Output[row] = Gelu(GateSums[lane]) * UpSums[lane];
    }
}
