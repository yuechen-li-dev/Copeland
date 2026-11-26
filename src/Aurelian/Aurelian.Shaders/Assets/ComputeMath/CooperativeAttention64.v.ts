import { KernelParameters } from "./KernelMath";

// One subgroup streams a 16-query tile across 16-key tiles of a single head.
@compute
@numthreads(32, 1, 1)
function CooperativeAttention64Main(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Queries: WorkgroupArray<u32, 512>,
    @workgroup readwrite Operand: WorkgroupArray<u32, 512>,
    @workgroup readwrite Scores: WorkgroupArray<f32, 256>,
    @workgroup readwrite Probability: WorkgroupArray<u32, 128>,
    @workgroup readwrite Product: WorkgroupArray<f32, 1024>,
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
    static for (let row of Indices<16>()) {
        Queries[row * 32 + lane] = Query[(firstQuery + row) * stride + pairOffset];
    }
    var maximum: f32 = -3.402823466e38;
    var total: f32 = 0.0;
    var outputSum: Array<f32, 32> = [
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0
    ];
    for (let block of BoundedIndices<128>(p.inner)) {
        let firstKey = block * 16;
        static for (let row of Indices<16>()) {
            var key: u32 = 0;
            if (firstKey + row < p.columns) {
                key = KeyValue[(firstKey + row) * stride + pairOffset];
            }
            Operand[row * 32 + lane] = key;
        }
        WorkgroupBarrier();
        CooperativeMatMul(Queries, Operand, Scores, true);
        WorkgroupBarrier();
        if (lane < 16) {
            let previous = maximum;
            static for (let j of Indices<16>()) {
                if (firstKey + j < p.columns) {
                    maximum = Max(maximum, Scores[lane * 16 + j] * 0.125);
                }
            }
            let factor = Exp(previous - maximum);
            Scale[lane] = factor;
            total = total * factor;
            static for (let j of Indices<8>()) {
                var low: f32 = 0.0;
                var high: f32 = 0.0;
                if (firstKey + j * 2 < p.columns) {
                    low = Exp(Scores[lane * 16 + j * 2] * 0.125 - maximum);
                }
                if (firstKey + j * 2 + 1 < p.columns) {
                    high = Exp(Scores[lane * 16 + j * 2 + 1] * 0.125 - maximum);
                }
                total = total + low + high;
                Probability[lane * 8 + j] = PackFloat16Pair(low, high);
            }
            Denominator[lane] = total;
        }
        static for (let row of Indices<16>()) {
            var value: u32 = 0;
            if (firstKey + row < p.columns) {
                value = KeyValue[(p.columns + firstKey + row) * stride + pairOffset];
            }
            Operand[row * 32 + lane] = value;
        }
        WorkgroupBarrier();
        CooperativeMatMul(Probability, Operand, Product, false);
        WorkgroupBarrier();
        static for (let part of Indices<32>()) {
            outputSum[part] = outputSum[part] * Scale[part / 2] + Product[part * 32 + lane];
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
