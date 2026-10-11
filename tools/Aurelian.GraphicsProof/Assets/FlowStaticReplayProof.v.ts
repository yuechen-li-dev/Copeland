import { Replay } from "./FlowExecution";

@compute
@numthreads(1, 1, 1)
function Main(@binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readwrite Output: StorageBuffer<f32>): void {
    // A closed replay is an authoring-time fact; dynamic FLOW remains runtime work.
    Output[0] = Convert<f32>(static Replay());
}
