import { KernelParameters } from "./KernelMath";
import { Q4BlockAddress, Q4Scale } from "./PackedQ4Address";

// Four independent K partials keep each warp on adjacent nibble planes.
@compute
@numthreads(128, 1, 1)
function QuantizedQ4TiledMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Sums: WorkgroupArray<f32, 128>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Weights: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let rowInGroup = lane % 8;
    let row = group.x * 8 + rowInGroup;
    let channel = lane / 8;
    let firstTile = group.y * p.width;
    var sums: Array<f32, 4> = [0.0, 0.0, 0.0, 0.0];
    for (let tile of BoundedIndices<64>(p.width)) {
        let start = (firstTile + tile) * 512 + channel * 8;
        let startAddress = Q4BlockAddress(row, start, p.inner, 8, 4);
        static for (let part of Indices<4>()) {
            let first = start + part * 128;
            let address = startAddress + part * 144;
            let word = Weights[address + 4 + channel % 4 * 8 + rowInGroup];
            var dot: f32 = 0.0;
            static for (let component of Indices<8>()) {
                let nibble = (word >> (component * 4)) & 15;
                dot = dot + Input[first + component] * (Convert<f32>(nibble) - 8.0);
            }
            sums[part] = sums[part] + dot * Q4Scale(Weights, address, rowInGroup, 8, 4);
        }
    }
    Sums[lane] = sums[0] + sums[1] + sums[2] + sums[3];
    WorkgroupBarrier();
    static for (let stage of Indices<4>()) {
        let stride = 8 >> stage;
        if (channel < stride) {
            Sums[lane] = Sums[lane] + Sums[lane + stride * 8];
        }
        WorkgroupBarrier();
    }
    if (channel == 0) {
        Output[group.y * p.columns + row] = Sums[lane];
    }
}
