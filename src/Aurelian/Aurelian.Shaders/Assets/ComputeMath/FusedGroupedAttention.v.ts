import { KernelParameters } from "./KernelMath";
import { ScoreLanes, SplitDecode } from "./AttentionStripe32";

// One workgroup owns one query head. Each stripe computes one key score.
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
    var queryStart = (group.y * p.height + group.x) * p.width;
    let available = p.offset + group.y + 1;
    var firstKey: u32 = 0;
    if (available > p.mode) { firstKey = available - p.mode; }
    var count = available - firstKey;
    if (SplitDecode) {
        queryStart = group.x * p.width;
        let tileOffset = group.y * p.stride;
        firstKey = p.offset + tileOffset;
        count = p.columns - tileOffset;
        if (count > p.stride) { count = p.stride; }
    }
    let stripe = lane / ScoreLanes;
    let channel = lane % ScoreLanes;
    for (let block of BoundedIndices<256>(p.inner)) {
        let key = block * (128 / ScoreLanes) + stripe;
        var sum: f32 = 0.0;
        if (key < count) {
            let keyStart = (firstKey + key) * p.width;
            static for (let part of Indices<32>()) {
                let index = part * ScoreLanes + channel;
                if (part < 512 / ScoreLanes && index < p.width) {
                    sum = sum + Query[queryStart + index] * Cache[keyStart + index];
                }
            }
        }
        Partial[lane] = sum;
        WorkgroupBarrier();
        if (channel == 0 && key < count) {
            var dot: f32 = 0.0;
            static for (let component of Indices<32>()) {
                if (component < ScoreLanes) { dot = dot + Partial[stripe * ScoreLanes + component]; }
            }
            Scores[key] = dot;
        }
        WorkgroupBarrier();
    }

    var maximum: f32 = -3.402823466e38;
    static for (let part of Indices<8>()) {
        let key = part * 128 + lane;
        if ((!SplitDecode || part == 0) && key < count) { maximum = Max(maximum, Scores[key]); }
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
        if ((!SplitDecode || part == 0) && key < count) {
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
        static for (let item of Indices<8>()) {
            let key = block * (128 / ScoreLanes) + item;
            if (item < 128 / ScoreLanes && key < count) {
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
    let partialStart = (group.x * p.channels + group.y) * (p.width + 2);
    if (SplitDecode && lane == 0) {
        Output[partialStart] = rowMax;
        Output[partialStart + 1] = denom;
    }
    static for (let part of Indices<4>()) {
        let index = part * 128 + lane;
        if (index < p.width) {
            if (SplitDecode) {
                Output[partialStart + 2 + index] = result[part];
            } else {
                Output[queryStart + index] = result[part] / denom;
            }
        }
    }
}
