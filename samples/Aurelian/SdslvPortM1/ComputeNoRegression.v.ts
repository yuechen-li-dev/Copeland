@compute
@numthreads(8, 1, 1)
function ComputeNoRegression_CS(
    @builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readwrite Output: StorageBuffer<f32>
): void {
    const index: u32 = thread.x;
    Output[index] = Input[index];
    return;
}
