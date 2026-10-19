import { KernelParameters, Silu } from "./KernelMath";

// Four contiguous 256-lane passes amortize workgroup scheduling. Lanes within
// each pass read adjacent elements; masked tails support every channel width.
@compute
@numthreads(256, 1, 1)
function BiasSiluMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Bias: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let base = (group.x + group.y * p.height) * 1024;
    static for (let part of Indices<4>()) {
        let index = base + lane + part * 256;
        if (index < p.rows * p.columns) {
            Output[index] = Silu(Input[index] + Bias[index % p.columns]);
        }
    }
}
