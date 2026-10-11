import { ReplayCompletion } from "./FlowExecution";

@compute @numthreads(8, 1, 1)
function Main(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readwrite Output: StorageBuffer<f32>): void {
    if (thread.x >= 6) {
        return;
    }
    Output[thread.x] = Convert<f32>(ReplayCompletion(U32(Input[thread.x])));
}
