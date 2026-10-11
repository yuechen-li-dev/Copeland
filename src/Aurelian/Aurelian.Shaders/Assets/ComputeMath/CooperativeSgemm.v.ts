import { KernelParameters } from "./KernelMath";

// Optional Prometheus M40a KHR subgroup tuple: packed FP16 input, FP32 output.
// The host must negotiate the exact tuple and reject every non-16-aligned shape.
@compute
@numthreads(32, 1, 1)
function CooperativeSgemmMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @binding(0) readonly A: StorageBuffer<u32>,
    @binding(1) readonly B: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    CooperativeMatMul(A, B, Output, group, lane, p.rows, p.columns, p.inner);
}
