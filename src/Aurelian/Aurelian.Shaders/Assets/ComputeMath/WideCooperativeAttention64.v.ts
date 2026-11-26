import { KeyTileWidth } from "./KeyTile32";
import { KernelParameters } from "./KernelMath";

// One subgroup owns 16 query rows. Two lanes cooperate on each softmax row.
// Left holds queries during QK and probabilities during PV; Result is reused too.
@compute
@numthreads(32, 1, 1)
function WideCooperativeAttention64Main(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Left: WorkgroupArray<u32, 512>,
    @workgroup readwrite Right: WorkgroupArray<u32, 2048>,
    @workgroup readwrite Result: WorkgroupArray<f32, 1024>,
    @workgroup readwrite PartialSum: WorkgroupArray<f32, 32>,
    @workgroup readwrite Partial: WorkgroupArray<f32, 32>,
    @workgroup readwrite Scale: WorkgroupArray<f32, 16>,
    @workgroup readwrite Denominator: WorkgroupArray<f32, 16>,
    @binding(0) readonly Query: StorageBuffer<u32>,
    @binding(1) readonly KeyValue: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let firstQuery = group.x * 16;
    let pairOffset = group.y * 32 + lane;
    let stride = p.width / 2;
    let rowIndex = lane % 16;
    let side = lane / 16;
    let tileWidth = KeyTileWidth();
    var maximum: f32 = -3.402823466e38;
    var total: f32 = 0.0;
    var outputSum: Array<f32, 32> = [
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0
    ];
    for (let block of BoundedIndices<64>(p.inner)) {
        let firstKey = block * tileWidth;
        static for (let row of Indices<16>()) {
            Left[row * 32 + lane] = Query[(firstQuery + row) * stride + pairOffset];
        }
        static for (let row of Indices<64>()) {
            if (row < tileWidth) {
                var key: u32 = 0;
                if (firstKey + row < p.columns) {
                    key = KeyValue[(firstKey + row) * stride + pairOffset];
                }
                Right[row * 32 + lane] = key;
            }
        }
        WorkgroupBarrier();
        CooperativeMatMul(Left, Right, Result, true, p.mode);
        WorkgroupBarrier();
        let previous = maximum;
        static for (let j of Indices<16>()) {
            if (j < tileWidth / 4) {
                let col = (j * 2 + side) * 2;
                if (firstKey + col < p.columns) {
                    maximum = Max(maximum, Result[rowIndex * tileWidth + col] * 0.125);
                }
                if (firstKey + col + 1 < p.columns) {
                    maximum = Max(maximum, Result[rowIndex * tileWidth + col + 1] * 0.125);
                }
            }
        }
        Partial[lane] = maximum;
        WorkgroupBarrier();
        maximum = Max(Partial[rowIndex], Partial[rowIndex + 16]);
        let factor = Exp(previous - maximum);
        var weightSum: f32 = 0.0;
        static for (let j of Indices<16>()) {
            if (j < tileWidth / 4) {
                let pair = j * 2 + side;
                let col = pair * 2;
                var low: f32 = 0.0;
                var high: f32 = 0.0;
                if (firstKey + col < p.columns) {
                    low = Exp(Result[rowIndex * tileWidth + col] * 0.125 - maximum);
                }
                if (firstKey + col + 1 < p.columns) {
                    high = Exp(Result[rowIndex * tileWidth + col + 1] * 0.125 - maximum);
                }
                weightSum = weightSum + low + high;
                Left[rowIndex * (tileWidth / 2) + pair] = PackFloat16Pair(low, high);
            }
        }
        static for (let row of Indices<64>()) {
            if (row < tileWidth) {
                var value: u32 = 0;
                if (firstKey + row < p.columns) {
                    value = KeyValue[(p.columns + firstKey + row) * stride + pairOffset];
                }
                Right[row * 32 + lane] = value;
            }
        }
        PartialSum[lane] = weightSum;
        WorkgroupBarrier();
        if (side == 0) {
            total = total * factor + PartialSum[rowIndex] + PartialSum[rowIndex + 16];
            Denominator[rowIndex] = total;
            Scale[rowIndex] = factor;
        }
        CooperativeMatMul(Left, Right, Result, false, p.mode);
        WorkgroupBarrier();
        static for (let part of Indices<32>()) {
            outputSum[part] = outputSum[part] * Scale[part / 2] + Result[part * 32 + lane];
        }
        WorkgroupBarrier();
    }
    static for (let part of Indices<32>()) {
        let row = firstQuery + part / 2;
        if (row < p.rows) {
            Output[row * p.width + group.y * 64 + (part % 2) * 32 + lane] = outputSum[part] / Denominator[part / 2];
        }
    }
}
