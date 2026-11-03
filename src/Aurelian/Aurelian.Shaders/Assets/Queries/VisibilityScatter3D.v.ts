import { VisibilityCandidate, IndexedCommand, PreparedLod } from "./VisibilityTypes";

@compute
@numthreads(64, 1, 1)
function VisibilityScatterMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Candidates: StorageBuffer<VisibilityCandidate>,
    @binding(1) readonly Ranks: StorageBuffer<u32>,
    @binding(2) readonly Offsets: StorageBuffer<u32>,
    @binding(3) readwrite Commands: StorageBuffer<IndexedCommand>): void {
    let packedRank = Ranks[thread.x];
    let rank = packedRank & 255;
    if (rank > 0) {
        let candidate = Candidates[thread.x];
        let lod = PreparedLod(candidate, packedRank >> 8);
        let destination = Offsets[thread.x / 64] + rank - 1;
        Commands[destination] = { indexCount: lod.indexCount, instanceCount: 1,
            firstIndex: lod.firstIndex, vertexOffset: 0, firstInstance: candidate.firstInstance };
    }
}
