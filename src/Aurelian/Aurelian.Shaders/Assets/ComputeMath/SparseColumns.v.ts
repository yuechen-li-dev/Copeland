import { KernelParameters } from "./KernelMath";

// Checkpoint layout is O/kX/kY/kZ/channel. uint.MaxValue denotes absent neighbors.
@compute
@numthreads(64, 1, 1)
function SparseColumnsMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Neighbors: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = thread.x + thread.y * p.stride;
    if (index >= p.rows * p.columns) { return; }
    let row = index / p.columns + p.offset;
    let columnIndex = index % p.columns;
    let neighbor = Neighbors[row * 27 + columnIndex / p.inner];
    var value: f32 = 0.0;
    if (neighbor < p.height) {
        value = Input[neighbor * p.inner + columnIndex % p.inner];
    }
    Output[index] = value;
}
