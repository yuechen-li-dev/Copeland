import { KernelParameters } from "./KernelMath";
import { Q4Rows, Q4ScaleWords } from "./Q4Rows1";
import { Q4BlockAddress, Q4Scale } from "./PackedQ4Address";

// Decode consecutive weight pairs, then transpose the 16x16 tile in shared memory.
@compute
@numthreads(64, 1, 1)
function PackQ4WeightsHalfMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Tile: WorkgroupArray<u32, 128>,
    @binding(0) readonly Weights: StorageBuffer<u32>,
    @binding(1) readonly Auxiliary: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<u32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    static for (let part of Indices<2>()) {
        let index = lane + part * 64;
        let inner = group.x * 16 + index % 8 * 2 + p.offset;
        let row = p.height + group.y * 16 + index / 8;
        let address = Q4BlockAddress(row, inner, p.inner, Q4Rows, Q4ScaleWords);
        let rowInBlock = row % Q4Rows;
        let scale = Q4Scale(Weights, address, rowInBlock, Q4Rows, Q4ScaleWords);
        let wordAddress = address + Q4ScaleWords + inner % 32 / 8 * Q4Rows + rowInBlock;
        let word = Weights[wordAddress] >> (inner % 8 * 4);
        let low = (Convert<f32>(word & 15) - 8.0) * scale;
        let high = (Convert<f32>((word >> 4) & 15) - 8.0) * scale;
        Tile[index] = PackFloat16Pair(low, high);
    }
    WorkgroupBarrier();
    static for (let part of Indices<2>()) {
        let index = lane + part * 64;
        let inner = index / 8;
        let pair = index % 8;
        let source = pair * 16 + inner / 2;
        let shift = inner % 2 * 16;
        let low = (Tile[source] >> shift) & 65535;
        let high = (Tile[source + 8] >> shift) & 65535;
        Output[(group.x * 16 + inner) * (p.columns / 2) + group.y * 8 + pair] = low | (high << 16);
    }
}
