import { KernelParameters } from "./KernelMath";

@compute
@numthreads(128, 1, 1)
function MergeProjectionPartialsMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Partials: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    if (thread.x >= p.columns) { return; }
    var sum: f32 = 0.0;
    for (let part of BoundedIndices<4>(p.rows)) {
        sum = sum + Partials[part * p.columns + thread.x];
    }
    Output[thread.x] = sum;
}
