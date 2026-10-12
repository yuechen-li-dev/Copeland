import { KernelParameters } from "./KernelMath";
import { BFloat16 } from "./BFloat16";

@compute
@numthreads(64, 1, 1)
function GroupedAttentionMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Sums: WorkgroupArray<f32, 64>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Cache: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let token = group.y / p.height;
    let head = group.y % p.height;
    let kvHead = head / (p.height / p.channels);
    var sum: f32 = 0.0;
    if (p.mode == 0) {
        static for (let tile of Indices<8>()) {
            let channel = lane + tile * 64;
            if (channel < p.width) {
                sum = sum + Input[(token * p.height + head) * p.width + channel]
                    * Cache[(group.x * p.channels + kvHead) * p.width + channel];
            }
        }
    } else {
        static for (let tile of Indices<16>()) {
            let key = lane + tile * 64;
            if (key < p.columns) {
                sum = sum + Input[group.y * p.columns + key]
                    * Cache[(key * p.channels + kvHead) * p.width + group.x];
            }
        }
    }
    Sums[lane] = sum;
    WorkgroupBarrier();
    static for (let stage of Indices<6>()) {
        let stride = 32 >> stage;
        if (lane < stride) { Sums[lane] = Sums[lane] + Sums[lane + stride]; }
        WorkgroupBarrier();
    }
    if (lane == 0) {
        if (p.mode == 0) {
            var result: f32 = BFloat16(Sums[0]);
            let position = token + p.offset;
            if (group.x > position || group.x + p.stride <= position) { result = -3.402823466e38; }
            Output[group.y * p.columns + group.x] = result;
        } else {
            Output[group.y * p.width + group.x] = BFloat16(Sums[0]);
        }
    }
}
