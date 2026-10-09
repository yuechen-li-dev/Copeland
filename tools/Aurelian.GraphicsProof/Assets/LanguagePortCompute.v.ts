import { Shade } from "./LanguagePortLibrary";

@compute
@numthreads(8, 1, 1)
function LanguageMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readwrite Output: StorageBuffer<f32>): void {
    const index: u32 = thread.x;
    if (index < 5) {
        Output[index] = Shade(Input[index]);
    }
}
