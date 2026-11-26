import { KernelParameters } from "./KernelMath";

// One workgroup owns one query head. A 32-lane stripe computes each score.
@compute
@numthreads(128, 1, 1)
function FusedGroupedAttentionMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Partial: WorkgroupArray<f32, 128>,
    @workgroup readwrite Scores: WorkgroupArray<f32, 1024>,
    @binding(0) readonly Query: StorageBuffer<f32>,
    @binding(1) readonly Cache: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let queryStart = (group.y * p.height + group.x) * p.width;
    let available = p.offset + group.y + 1;
    var firstKey: u32 = 0;
    if (available > p.mode) { firstKey = available - p.mode; }
    let count = available - firstKey;
    let stripe = lane / 32;
    let channel = lane % 32;
    for (let block of BoundedIndices<256>(p.inner)) {
        let key = block * 4 + stripe;
        var sum: f32 = 0.0;
        if (key < count) {
            let keyStart = (firstKey + key) * p.width;
            static for (let part of Indices<16>()) {
                let index = part * 32 + channel;
                if (index < p.width) {
                    sum = sum + Query[queryStart + index] * Cache[keyStart + index];
                }
            }
        }
        Partial[lane] = sum;
        WorkgroupBarrier();
        if (channel == 0 && key < count) {
            var dot: f32 = 0.0;
            static for (let component of Indices<32>()) {
                dot = dot + Partial[stripe * 32 + component];
            }
            Scores[key] = dot;
        }
        WorkgroupBarrier();
    }

    var maximum: f32 = -3.402823466e38;
    static for (let part of Indices<8>()) {
        let key = part * 128 + lane;
        if (key < count) { maximum = Max(maximum, Scores[key]); }
    }
    Partial[lane] = maximum;
    WorkgroupBarrier();
    static for (let stage of Indices<7>()) {
        let stride = 64 >> stage;
        if (lane < stride) { Partial[lane] = Max(Partial[lane], Partial[lane + stride]); }
        WorkgroupBarrier();
    }
    let rowMax = Partial[0];
    WorkgroupBarrier();
    var weightSum: f32 = 0.0;
    static for (let part of Indices<8>()) {
        let key = part * 128 + lane;
        if (key < count) {
            let weight = Exp(Scores[key] - rowMax);
            Scores[key] = weight;
            weightSum = weightSum + weight;
        }
    }
    Partial[lane] = weightSum;
    WorkgroupBarrier();
    static for (let stage of Indices<7>()) {
        let stride = 64 >> stage;
        if (lane < stride) { Partial[lane] = Partial[lane] + Partial[lane + stride]; }
        WorkgroupBarrier();
    }

    var result: Array<f32, 4> = [0.0, 0.0, 0.0, 0.0];
    for (let block of BoundedIndices<256>(p.inner)) {
        static for (let item of Indices<4>()) {
            let key = block * 4 + item;
            if (key < count) {
                let valueStart = (p.rows + firstKey + key) * p.width;
                let weight = Scores[key];
                static for (let part of Indices<4>()) {
                    let index = part * 128 + lane;
                    if (index < p.width) {
                        result[part] = result[part] + Cache[valueStart + index] * weight;
                    }
                }
            }
        }
    }
    let denom = Partial[0];
    static for (let part of Indices<4>()) {
        let index = part * 128 + lane;
        if (index < p.width) { Output[queryStart + index] = result[part] / denom; }
    }
}
