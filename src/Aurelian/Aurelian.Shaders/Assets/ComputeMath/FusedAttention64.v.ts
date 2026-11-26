import { KernelParameters } from "./KernelMath";

// Noncausal FP32 attention. A workgroup owns 16 queries of one head, streams
// 32 keys at a time and keeps the unnormalized output in registers.
@compute
@numthreads(8, 8, 1)
function FusedAttention64Main(@builtin(groupId) group: uint3,
    @builtin(groupThreadId) local: uint3,
    @workgroup readwrite Queries: WorkgroupArray<f32, 1040>,
    @workgroup readwrite Tile: WorkgroupArray<f32, 2112>,
    @workgroup readwrite Scores: WorkgroupArray<f32, 512>,
    @workgroup readwrite Maximum: WorkgroupArray<f32, 16>,
    @workgroup readwrite Scale: WorkgroupArray<f32, 16>,
    @workgroup readwrite Denominator: WorkgroupArray<f32, 16>,
    @binding(0) readonly Query: StorageBuffer<f32>,
    @binding(1) readonly KeyValue: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let lane = local.y * 8 + local.x;
    let head = group.y * 64;
    let firstQuery = group.x * 16;
    for (let part of BoundedIndices<16>(16)) {
        let row = firstQuery + part;
        var q: f32 = 0.0;
        if (row < p.rows) {
            q = Query[row * p.width + head + lane];
        }
        Queries[part * 65 + lane] = q;
    }
    if (lane < 16) {
        Maximum[lane] = -3.402823466e38;
        Denominator[lane] = 0.0;
    }
    var outputSum: Array<f32, 16> = [
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0
    ];
    for (let block of BoundedIndices<64>(p.inner)) {
        let firstKey = block * 32;
        for (let part of BoundedIndices<32>(32)) {
            var k: f32 = 0.0;
            if (firstKey + part < p.columns) {
                k = KeyValue[(firstKey + part) * p.width + head + lane];
            }
            Tile[lane * 33 + part] = k;
        }
        WorkgroupBarrier();
        var dots: Array<f32, 8> = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0];
        for (let d of BoundedIndices<64>(64)) {
            let a = Queries[local.y * 65 + d];
            let b = Queries[(local.y + 8) * 65 + d];
            static for (let slot of Indices<4>()) {
                let k = Tile[d * 33 + local.x + slot * 8];
                dots[slot] = dots[slot] + a * k;
                dots[slot + 4] = dots[slot + 4] + b * k;
            }
        }
        static for (let r of Indices<2>()) {
            let row = local.y + r * 8;
            static for (let j of Indices<4>()) {
                let key = local.x + j * 8;
                var score: f32 = -3.402823466e38;
                if (firstKey + key < p.columns) {
                    score = dots[r * 4 + j] * 0.125;
                }
                Scores[row * 32 + key] = score;
            }
        }
        WorkgroupBarrier();
        if (lane < 16) {
            let previous = Maximum[lane];
            var next = previous;
            for (let j of BoundedIndices<32>(32)) {
                next = Max(next, Scores[lane * 32 + j]);
            }
            Maximum[lane] = next;
            Scale[lane] = Exp(previous - next);
        }
        for (let part of BoundedIndices<32>(32)) {
            var v: f32 = 0.0;
            if (firstKey + part < p.columns) {
                v = KeyValue[(p.columns + firstKey + part) * p.width + head + lane];
            }
            Tile[part * 64 + lane] = v;
        }
        WorkgroupBarrier();
        static for (let r of Indices<2>()) {
            let row = local.y + r * 8;
            static for (let j of Indices<4>()) {
                let index = row * 32 + local.x + j * 8;
                Scores[index] = Exp(Scores[index] - Maximum[row]);
            }
            static for (let c of Indices<8>()) {
                const cell = r * 8 + c;
                outputSum[cell] = outputSum[cell] * Scale[row];
            }
        }
        WorkgroupBarrier();
        if (lane < 16) {
            var total = Denominator[lane] * Scale[lane];
            for (let j of BoundedIndices<32>(32)) {
                total = total + Scores[lane * 32 + j];
            }
            Denominator[lane] = total;
        }
        for (let j of BoundedIndices<32>(32)) {
            let a = Scores[local.y * 32 + j];
            let b = Scores[(local.y + 8) * 32 + j];
            static for (let c of Indices<8>()) {
                let v = Tile[j * 64 + local.x + c * 8];
                outputSum[c] = outputSum[c] + a * v;
                outputSum[c + 8] = outputSum[c + 8] + b * v;
            }
        }
        WorkgroupBarrier();
    }
    static for (let r of Indices<2>()) {
        let row = local.y + r * 8;
        if (firstQuery + row < p.rows) {
            static for (let c of Indices<8>()) {
                Output[(firstQuery + row) * p.width + head + local.x + c * 8] = outputSum[r * 8 + c] / Denominator[row];
            }
        }
    }
}
