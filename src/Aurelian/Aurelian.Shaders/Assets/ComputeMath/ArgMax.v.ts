import { KernelParameters } from "./KernelMath";

// IEEE binary32 order for non-NaN values, with a common key for both zeros.
function FloatRank(bits: u32): u32 {
    let sign: u32 = 1 << 31;
    let magnitude = bits & (sign - 1);
    if (magnitude == 0) { return sign; }
    if ((bits & sign) == 0) { return bits | sign; }
    return (sign - 1) - magnitude;
}

function PreferCandidate(score: f32, index: u32, bestScore: f32, bestIndex: u32): bool {
    let bits = Bitcast<u32>(score);
    let key = FloatRank(bits);
    let bestKey = FloatRank(Bitcast<u32>(bestScore));
    let eligible = (bits & 2147483647) <= 2139095040;
    return when utility {
        case true when eligible && (key != bestKey || index < bestIndex) score key
        case false when true score bestKey
        else false
    };
}

@compute
@numthreads(256, 1, 1)
function ArgMaxMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Scores: WorkgroupArray<f32, 256>,
    @workgroup readwrite IndicesByLane: WorkgroupArray<u32, 256>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    var bestScore: f32 = -3.402823466e38;
    var bestIndex: u32 = 1048576;
    static for (let part of Indices<4>()) {
        let item = group.x * 1024 + lane + part * 256;
        if (item < p.columns) {
            var score = Input[item];
            var index = item;
            if (p.mode == 1) {
                score = Input[item * 2];
                index = U32(Input[item * 2 + 1]);
            }
            if (PreferCandidate(score, index, bestScore, bestIndex)) {
                bestScore = score;
                bestIndex = index;
            }
        }
    }
    Scores[lane] = bestScore;
    IndicesByLane[lane] = bestIndex;
    WorkgroupBarrier();
    static for (let stage of Indices<8>()) {
        let stride = 128 >> stage;
        if (lane < stride) {
            let other = lane + stride;
            let score = Scores[other];
            let index = IndicesByLane[other];
            if (PreferCandidate(score, index, Scores[lane], IndicesByLane[lane])) {
                Scores[lane] = score;
                IndicesByLane[lane] = index;
            }
        }
        WorkgroupBarrier();
    }
    if (lane == 0) {
        Output[group.x * 2] = Scores[0];
        Output[group.x * 2 + 1] = Convert<f32>(IndicesByLane[0]);
    }
}
