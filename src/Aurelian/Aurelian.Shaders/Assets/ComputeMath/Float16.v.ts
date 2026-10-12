// Round binary16 to nearest, ties to even, while retaining FP32 storage.
// Includes half subnormals, signed zero and overflow to half infinity.
export function Float16(value: f32): f32 {
    let magnitude = Abs(value);
    if (magnitude >= 65520.0) {
        let sign = Bitcast<u32>(value) & (32768 << 16);
        return Bitcast<f32>(sign | 2139095040);
    }
    if (magnitude >= 0.00006103515625) {
        let bits = Bitcast<u32>(value);
        let parity = bits >> 13 & 1;
        let rounded = (bits + 4095 + parity) & (65535 << 16 | 57344);
        return Bitcast<f32>(rounded);
    }
    let scaled = magnitude * 16777216.0;
    var integral = Floor(scaled);
    let fraction = scaled - integral;
    let odd = integral - Floor(integral * 0.5) * 2.0;
    if (fraction > 0.5 || (fraction == 0.5 && odd != 0.0)) {
        integral = integral + 1.0;
    }
    let result = integral / 16777216.0;
    let sign = Bitcast<u32>(value) & (32768 << 16);
    return Bitcast<f32>(Bitcast<u32>(result) | sign);
}
