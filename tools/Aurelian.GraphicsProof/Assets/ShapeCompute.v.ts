import { Shade, DynamicValue } from "./ShapeLibrary";

@compute
@numthreads(8, 1, 1)
function Main(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly input: StorageBuffer<f32>,
    @binding(1) readwrite output: StorageBuffer<f32>): void {
    if (thread.x < 5) {
        output[thread.x] = Shade(input[thread.x]) + DynamicValue(thread.x);
    }
}
