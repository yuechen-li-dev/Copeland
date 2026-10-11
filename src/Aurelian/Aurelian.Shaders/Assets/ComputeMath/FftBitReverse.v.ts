import { KernelParameters } from "./KernelMath";

@compute
@numthreads(64, 1, 1)
function BitReverseMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<float2>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<float2>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    if (thread.x >= p.rows * p.columns) {
        return;
    }
    let batch = thread.x / p.columns;
    let element = thread.x % p.columns;
    var reverse: u32 = 0;
    static for (let bit of Indices<16>()) {
        if (bit < p.offset) {
            reverse = (reverse << 1) | ((element >> bit) & 1);
        }
    }
    Output[thread.x] = Input[batch * p.columns + reverse];
}
