import { KernelParameters } from "./KernelMath";

// Keys occupy the first half of the cache, values the second.
@compute
@numthreads(64, 1, 1)
function DecodeQkvMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Qkv: StorageBuffer<f32>,
    @binding(1) readwrite Cache: StorageBuffer<f32>,
    @binding(2) readwrite Query: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let c = thread.x;
    if (c < p.width) {
        Query[c] = Qkv[c] * p.alpha;
        Cache[p.offset * p.width + c] = Qkv[p.width + c];
        Cache[(p.rows + p.offset) * p.width + c] = Qkv[2 * p.width + c];
    }
}
