import { Replay } from "./FlowExecution";

@compute @numthreads(1, 1, 1)
function Main(@binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readwrite Output: StorageBuffer<f32>): void {
    Output[0] = Convert<f32>(Replay());
}
