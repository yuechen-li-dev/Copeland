import { KernelParameters } from "./KernelMath";

// Load binary16 fragments directly from packed weights and activations.
// Mode 1 produces one output tile; mode 2 reuses operands across a 2 x 2 tile block.
@compute
@numthreads(32, 1, 1)
function CooperativeProjectionMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @binding(0) readonly A: StorageBuffer<u32>,
    @binding(1) readonly B: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    CooperativeMatMul(A, B, Output, group, lane, p.rows, p.columns, p.inner, p.mode);
}
