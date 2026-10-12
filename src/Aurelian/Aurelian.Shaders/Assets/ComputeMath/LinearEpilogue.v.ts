import { KernelParameters } from "./KernelMath";

@compute
@numthreads(64, 1, 1)
function LinearEpilogueMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Weights: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = thread.x + thread.y * p.stride;
    if (index < p.rows * p.columns) {
        let row = index / p.columns;
        let columnIndex = index % p.columns;
        let destination = row * p.width + p.offset + columnIndex;
        var value = Input[row * p.inner + columnIndex];
        if (p.mode != 0) {
            value = value + Output[destination];
        }
        if (p.padding != 0) {
            value = value + Weights[columnIndex * p.channels + p.channels - 1];
        }
        Output[destination] = value;
    }
}
