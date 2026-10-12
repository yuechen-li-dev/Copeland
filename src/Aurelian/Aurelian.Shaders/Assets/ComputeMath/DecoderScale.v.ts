import { KernelParameters } from "./KernelMath";
import { BFloat16 } from "./BFloat16";

@compute
@numthreads(64, 1, 1)
function DecoderScaleMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    if (thread.x < p.rows * p.columns) {
        Output[thread.x] = BFloat16(Input[thread.x] * p.alpha);
    }
}
