import { KernelParameters, Gelu } from "./KernelMath";

@compute
@numthreads(64, 1, 1)
function DecoderPointwiseFp32Main(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = thread.x + thread.y * p.stride;
    if (index >= p.rows * p.columns) { return; }
    let value = Input[index];
    if (p.mode == 1) {
        let row = index / p.columns + p.offset;
        let columnIndex = index % p.columns + p.width;
        Output[index] = Gelu(value) * Auxiliary[row * p.inner + columnIndex];
    } else {
        Output[index] = value * p.alpha;
    }
}
