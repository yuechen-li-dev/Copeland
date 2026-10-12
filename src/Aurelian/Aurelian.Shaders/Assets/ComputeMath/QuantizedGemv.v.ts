import { KernelParameters } from "./KernelMath";
import { BFloat16 } from "./BFloat16";

// One complete dot product per workgroup. Each lane decodes a packed word once
// and reuses its scale for eight values; the host bounds K to 32768.
@compute
@numthreads(64, 1, 1)
function QuantizedGemvMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Sums: WorkgroupArray<f32, 64>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Weights: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    var sum: f32 = 0.0;
    static for (let tile of Indices<64>()) {
        let k = (lane + tile * 64) * 8;
        if (k < p.inner) {
            let element = group.x * p.inner + k;
            let block = element / 32 * 5;
            let scale = Bitcast<f32>(Weights[block]);
            let word = Weights[block + 1 + element % 32 / 8];
            static for (let component of Indices<8>()) {
                let nibble = word >> (component * 4) & 15;
                sum = sum + Input[group.y * p.inner + k + component]
                    * scale * (Convert<f32>(nibble) - 8.0);
            }
        }
    }
    Sums[lane] = sum;
    WorkgroupBarrier();
    static for (let stage of Indices<6>()) {
        let stride = 32 >> stage;
        if (lane < stride) {
            Sums[lane] = Sums[lane] + Sums[lane + stride];
        }
        WorkgroupBarrier();
    }
    if (lane == 0) {
        var result = Sums[0];
        if (p.mode == 1) {
            result = BFloat16(result);
        }
        Output[group.y * p.columns + group.x] = result;
    }
}
