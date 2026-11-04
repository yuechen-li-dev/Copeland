export function Read4(readonly data: StorageBuffer<f32>, offset: u32): float4 {
    return float4(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]);
}

// Normal attachment transport is packed RGBA16F, copied on the GPU. Decode
// IEEE binary16 explicitly; no half-precision arithmetic capability is required.
export function HalfToFloat(value: u32): f32 {
    let exponent: u32 = (value >> 10) & 31;
    let mantissa: u32 = value & 1023;
    let sign: u32 = (value & 32768) << 16;
    if (exponent == 0) {
        var result: f32 = Convert<f32>(mantissa) * 0.000000059604644775390625;
        if (sign != 0) { result = -result; }
        return result;
    }
    return Bitcast<f32>(sign | ((exponent + 112) << 23) | (mantissa << 13));
}

export function LightRandom(key: u32): f32 {
    var value: u32 = key;
    value = (value ^ (value >> 16)) * 0x7feb352d;
    value = (value ^ (value >> 15)) * 0x846ca68b;
    value = value ^ (value >> 16);
    return (Convert<f32>(value >> 8) + 0.5) / 16777216.0;
}

export function ReadSurfaceNormal(readonly normals: StorageBuffer<u32>, pixel: u32): float3 {
    let packedXY: u32 = normals[pixel * 2];
    let packedZW: u32 = normals[pixel * 2 + 1];
    return float3(
        HalfToFloat(packedXY & 65535),
        HalfToFloat(packedXY >> 16),
        HalfToFloat(packedZW & 65535));
}
