import { KernelParameters } from "./KernelMath";

@compute
@numthreads(64, 1, 1)
function BFloat16LookupMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Table: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = thread.x + thread.y * p.stride;
    if (index < p.columns) {
        let key = Bitcast<u32>(Input[index]) >> 16;
        Output[index] = Table[key];
    }
}
