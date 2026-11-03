import { VisibilityCamera } from "./VisibilityTypes";

@compute
@numthreads(1, 1, 1)
function VisibilityScanMain(@binding(0) readonly Blocks: StorageBuffer<u32>,
    @binding(1) readwrite Offsets: StorageBuffer<u32>,
    @binding(2) readwrite Count: StorageBuffer<u32>,
    @binding(3) readonly View: StorageBuffer<VisibilityCamera>): void {
    var total: u32 = 0;
    let p = View[0];
    for (let tile of BoundedIndices<512>((p.blockCount + 31) / 32)) {
        static for (let lane of Indices<32>()) {
            let index = tile * 32 + lane;
            if (index < p.blockCount) {
                Offsets[index] = total;
                total = total + Blocks[index];
            }
        }
    }
    Count[0] = total;
}
