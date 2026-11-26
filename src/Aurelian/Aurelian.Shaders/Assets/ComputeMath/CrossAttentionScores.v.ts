import { KernelParameters } from "./KernelMath";

// Four dot products over adjacent keys in the interleaved resident cache.
@compute
@numthreads(128, 1, 1)
function CrossAttentionScoresMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Sums: WorkgroupArray<f32, 128>,
    @binding(0) readonly Query: StorageBuffer<f32>,
    @binding(1) readonly Keys: StorageBuffer<f32>,
    @binding(2) readwrite Scores: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let key = group.x * 4 + lane / 32;
    let channel = lane % 32;
    let head = (group.y % p.height) * p.width;
    let token = group.y / p.height;
    let rowWidth = p.height * p.width;
    var dot: f32 = 0.0;
    if (key < p.columns) {
        static for (let tile of Indices<4>()) {
            let component = tile * 32 + channel;
            if (component < p.width) {
                let offset = head + component;
                dot = dot + Query[token * rowWidth + offset] * Keys[key * rowWidth + offset];
            }
        }
    }
    Sums[lane] = dot;
    WorkgroupBarrier();
    static for (let stage of Indices<5>()) {
        let offset = 16 >> stage;
        if (channel < offset) {
            Sums[lane] = Sums[lane] + Sums[lane + offset];
        }
        WorkgroupBarrier();
    }
    if (channel == 0 && key < p.columns) {
        Scores[group.y * p.columns + key] = Sums[lane] * p.alpha;
    }
}
