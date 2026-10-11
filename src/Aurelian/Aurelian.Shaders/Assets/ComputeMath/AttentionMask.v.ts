import { KernelParameters } from "./KernelMath";

@compute
@numthreads(64, 1, 1)
function MaskMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    if (thread.x >= p.rows * p.columns) {
        return;
    }
    let query = thread.x / p.columns;
    let key = thread.x % p.columns;
    var value = Input[thread.x];
    if (key > query + p.offset) {
        value = -1.0e30;
    }
    Output[thread.x] = value;
}
