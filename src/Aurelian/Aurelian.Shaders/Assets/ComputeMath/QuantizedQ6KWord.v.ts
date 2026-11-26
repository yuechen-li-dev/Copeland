import { KernelParameters } from "./KernelMath";
import { Q6Rows } from "./Q6Rows1";

function ReadWord(readonly weights: StorageBuffer<u32>, address: u32, word: u32, rowInGroup: u32): u32 {
    if (Q6Rows == 8) {
        return weights[address + word * 8 + rowInGroup];
    }
    let index = address / 4 + word;
    let low = weights[index];
    if (address % 4 == 0) {
        return low;
    }
    // Consecutive 210-byte GGUF blocks alternate between word and halfword alignment.
    return (low >> 16) | (weights[index + 1] << 16);
}

function DotCodes(packed: u32, activations: float4): f32 {
    let codes = float4(Convert<f32>(packed & 63), Convert<f32>((packed >> 8) & 63),
        Convert<f32>((packed >> 16) & 63), Convert<f32>(packed >> 24));
    return Dot(activations, codes - float4(32.0, 32.0, 32.0, 32.0));
}

@compute
@numthreads(128, 1, 1)
function QuantizedQ6KWordMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Sums: WorkgroupArray<f32, 128>,
    @binding(0) readonly Input: StorageBuffer<float4>,
    @binding(1) readonly Weights: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    var rowInGroup = lane / 16;
    var channel = lane % 16;
    if (Q6Rows == 8) {
        rowInGroup = lane % 8;
        channel = lane / 8;
    }
    let row = group.x * 8 + rowInGroup;
    // Each lane takes four consecutive values from each of four quadrants.
    let half = channel / 8;
    let word = channel % 8;
    var sum: f32 = 0.0;
    for (let block of BoundedIndices<32>(p.width)) {
        if (row < p.columns) {
            var address = (row * p.width + block) * 210;
            var halfScale: u32 = 0;
            if (Q6Rows == 8) {
                address = (group.x * p.width + block) * 420;
                halfScale = Weights[address + 416 + rowInGroup / 2] >> ((rowInGroup % 2) * 16);
            } else {
                halfScale = Weights[(address + 208) / 4] >> ((address % 4) * 8);
            }
            let lowA = ReadWord(Weights, address, half * 16 + word, rowInGroup);
            let lowB = ReadWord(Weights, address, half * 16 + word + 8, rowInGroup);
            let upper = ReadWord(Weights, address, 32 + half * 8 + word, rowInGroup);
            let scalesA = ReadWord(Weights, address, 48 + half * 2, rowInGroup);
            let scalesB = ReadWord(Weights, address, 49 + half * 2, rowInGroup);
            let first = block * 64 + half * 32 + word;
            var subtotal: f32 = 0.0;
            static for (let quadrant of Indices<4>()) {
                var low = lowA;
                if (quadrant % 2 != 0) { low = lowB; }
                let lowCodes = (low >> ((quadrant / 2) * 4)) & 0x0F0F0F0F;
                let highCodes = ((upper >> (quadrant * 2)) & 0x03030303) << 4;
                var scales = scalesA;
                if (quadrant >= 2) { scales = scalesB; }
                let scaleByte = (scales >> (((quadrant % 2) * 2 + word / 4) * 8)) & 255;
                let signedScale = Convert<f32>(scaleByte ^ 128) - 128.0;
                subtotal = subtotal + DotCodes(lowCodes | highCodes, Input[first + quadrant * 8]) * signedScale;
            }
            sum = sum + subtotal * UnpackFloat16(halfScale);
        }
    }
    Sums[lane] = sum;
    WorkgroupBarrier();
    static for (let stage of Indices<4>()) {
        let stride = 8 >> stage;
        if (channel < stride) {
            Sums[lane] = Sums[lane] + Sums[lane + stride * Q6Rows];
        }
        WorkgroupBarrier();
    }
    if (row < p.columns && channel == 0) {
        var result = Sums[lane];
        if (p.alpha > 0.0) {
            result = p.alpha * (2.0 / (1.0 + Exp(-2.0 * result / p.alpha)) - 1.0);
        }
        Output[p.offset + row] = result;
    }
}
