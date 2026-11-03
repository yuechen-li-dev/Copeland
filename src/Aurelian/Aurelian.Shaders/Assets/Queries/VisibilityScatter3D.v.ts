import { VisibilityCandidate, IndexedCommand } from "./VisibilityTypes";

@compute
@numthreads(64, 1, 1)
function VisibilityScatterMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Candidates: StorageBuffer<VisibilityCandidate>,
    @binding(1) readonly Ranks: StorageBuffer<u32>,
    @binding(2) readonly Offsets: StorageBuffer<u32>,
    @binding(3) readwrite Commands: StorageBuffer<IndexedCommand>): void {
    let rank = Ranks[thread.x];
    if (rank > 0) {
        let candidate = Candidates[thread.x];
        let destination = Offsets[thread.x / 64] + rank - 1;
        Commands[destination] = { indexCount: candidate.indexCount, instanceCount: 1,
            firstIndex: candidate.firstIndex, vertexOffset: 0, firstInstance: candidate.firstInstance };
    }
}
