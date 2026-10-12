import { KernelParameters } from "./KernelMath";
import { BFloat16 } from "./BFloat16";

// TRELLIS's F.normalize(x.float(), dim=-1) * gamma * sqrt(headDim), followed
// by a BF16 cast. Its clamp is on the L2 length, not RMSNorm's variance epsilon.
@compute
@numthreads(128, 1, 1)
function HeadNormalizeMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Squares: WorkgroupArray<f32, 128>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Gamma: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = (group.x + group.y * p.height) * 128 + lane;
    var value: f32 = 0.0;
    if (index < p.rows * p.columns) {
        value = Input[index];
    }
    Squares[lane] = value * value;
    WorkgroupBarrier();
    static for (let stage of Indices<7>()) {
        let stride = 64 >> stage;
        if (lane < stride) {
            Squares[lane] = Squares[lane] + Squares[lane + stride];
        }
        WorkgroupBarrier();
    }
    let denominator = Max(Sqrt(Squares[0]), 0.000000000001);
    if (index < p.rows * p.columns) {
        Output[index] = BFloat16(value / denominator * Gamma[index % p.columns] * Sqrt(128.0));
    }
}
