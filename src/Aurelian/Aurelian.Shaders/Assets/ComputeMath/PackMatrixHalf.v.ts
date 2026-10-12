import { KernelParameters } from "./KernelMath";
import { Float16 } from "./Float16";

function HalfBits(value: f32): u32 {
    let rounded = Float16(value);
    let bits = Bitcast<u32>(rounded);
    let sign = bits >> 16 & 32768;
    if (Abs(rounded) >= 65520.0) {
        return sign | 31744;
    }
    if (Abs(rounded) < 0.00006103515625) {
        return sign | U32(Abs(rounded) * 16777216.0);
    }
    let exponent = (bits >> 23 & 255) - 112;
    return sign | exponent << 10 | (bits >> 13 & 1023);
}

function Element(readonly input: StorageBuffer<f32>, p: KernelParameters, index: u32): f32 {
    let row = index / p.columns;
    let columnIndex = index % p.columns;
    if (p.mode == 0) {
        if (row < p.height && columnIndex + p.offset < p.inner) {
            return input[row * p.inner + p.offset + columnIndex];
        }
    } else {
        if (columnIndex < p.height && row + p.offset < p.inner - 1) {
            return input[columnIndex * p.inner + p.offset + row];
        }
    }
    return 0.0;
}

@compute
@numthreads(64, 1, 1)
function PackMatrixHalfMain(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly Input: StorageBuffer<f32>,
    @binding(1) readonly Auxiliary: StorageBuffer<f32>,
    @binding(2) readwrite Output: StorageBuffer<u32>,
    @binding(3) readonly Parameters: StorageBuffer<KernelParameters>): void {
    let p = Parameters[0];
    let index = thread.x + thread.y * p.stride;
    if (index * 2 < p.rows * p.columns) {
        let low = HalfBits(Element(Input, p, index * 2));
        let high = HalfBits(Element(Input, p, index * 2 + 1));
        Output[index] = low | high << 16;
    }
}
