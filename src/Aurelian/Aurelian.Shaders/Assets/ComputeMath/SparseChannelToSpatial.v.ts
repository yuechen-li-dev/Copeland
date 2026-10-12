import { KernelParameters } from "./KernelMath";

@compute
@numthreads(64, 1, 1)
function SparseChannelToSpatialMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Mapping: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = thread.x + thread.y * p.stride;
    if (index >= p.rows * p.columns) {
        return;
    }
    let child = index / p.columns + p.padding;
    let parent = Mapping[child * 2];
    let slot = Mapping[child * 2 + 1];
    var value: f32 = 0.0;
    if (parent >= p.offset && parent - p.offset < p.height && slot < 8) {
        let channelIndex = slot * (p.inner / 8) + index % p.columns / p.width;
        value = Input[(parent - p.offset) * p.inner + channelIndex];
    }
    Output[child * p.columns + index % p.columns] = value;
}
