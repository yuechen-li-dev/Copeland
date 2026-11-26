import { KernelParameters } from "./KernelMath";

function ByteAt(readonly weights: StorageBuffer<u32>, offset: u32): u32 {
    return (weights[offset / 4] >> ((offset % 4) * 8)) & 255;
}

@compute
@numthreads(128, 1, 1)
function QuantizedQ6KMain(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Sums: WorkgroupArray<f32, 128>,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Weights: StorageBuffer<u32>,
    @binding(2) readwrite Output: StorageBuffer<f32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let row = group.x * 4 + lane / 32;
    let channel = lane % 32;
    var sum: f32 = 0.0;
    for (let block of BoundedIndices<32>(p.width)) {
        if (row < p.columns) {
            let address = (row * p.width + block) * 210;
            let scale = UnpackFloat16(Weights[(address + 208) / 4] >> (((address + 208) % 4) * 8));
            var subtotal: f32 = 0.0;
            static for (let part of Indices<2>()) {
                let lowA = ByteAt(Weights, address + part * 64 + channel);
                let lowB = ByteAt(Weights, address + part * 64 + channel + 32);
                let upper = ByteAt(Weights, address + 128 + part * 32 + channel);
                static for (let quadrant of Indices<4>()) {
                    var low = lowA;
                    if (quadrant % 2 != 0) { low = lowB; }
                    low = (low >> ((quadrant / 2) * 4)) & 15;
                    let code = low | (((upper >> (quadrant * 2)) & 3) << 4);
                    let scaleByte = ByteAt(Weights, address + 192 + part * 8 + quadrant * 2 + channel / 16);
                    var signedScale = Convert<f32>(scaleByte);
                    if (scaleByte >= 128) { signedScale = signedScale - 256.0; }
                    let columnIndex = block * 256 + part * 128 + quadrant * 32 + channel;
                    subtotal = subtotal + Input[columnIndex] * signedScale * (Convert<f32>(code) - 32.0);
                }
            }
            sum = sum + subtotal * scale;
        }
    }
    Sums[lane] = sum;
    WorkgroupBarrier();
    static for (let stage of Indices<5>()) {
        let stride = 16 >> stage;
        if (channel < stride) {
            Sums[lane] = Sums[lane] + Sums[lane + stride];
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
