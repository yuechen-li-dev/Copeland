import { KernelParameters } from "./KernelMath";
import { ReadOperand } from "./Float16";

@compute
@numthreads(64, 1, 1)
function GatherPackedHalfMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = thread.x + thread.y * p.stride;
    if (index < p.rows * p.columns) {
        let source = (p.offset + index / p.columns) * p.inner + p.height + index % p.columns;
        Output[index] = ReadOperand<true>(Input, source);
    }
}
