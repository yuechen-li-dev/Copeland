// Round-to-nearest, ties-to-even BF16, represented in FP32 storage. This preserves
// signed zero and infinities and keeps NaNs as NaNs rather than truncating to infinity.
export function BFloat16(value: f32): f32 {
    let bits: u32 = Bitcast<u32>(value);
    let mask: u32 = 65535 << 16;
    let exponent: u32 = bits & 2139095040;
    if (exponent == 2139095040) {
        if ((bits & 8388607) != 0) {
            return Bitcast<f32>((bits | 4194304) & mask);
        }
        return value;
    }
    let rounded: u32 = bits + 32767 + ((bits >> 16) & 1);
    return Bitcast<f32>(rounded & mask);
}
