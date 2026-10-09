import { Field } from "./AetherisField";

@compute
@numthreads(8, 1, 1)
function FieldSamples(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readwrite Output: StorageBuffer<f32>): void {
    const index: u32 = thread.x;
    if (index < 1000) {
        const offset: u32 = index * 3;
        Output[index] = Field(Input[offset] * 1000.0, -Input[offset + 2] * 1000.0, Input[offset + 1] * 1000.0) * 0.001;
    }
}
