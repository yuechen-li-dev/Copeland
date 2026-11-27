// Four (ID/mass, FP32 weight) pairs, followed by FP32 position and normal.
// Slot zero also holds the quota. Admission gives every pixel at least one slot,
// so zero stored quota can mark a background pixel.
// 256 light IDs need 8 bits. Even nine reservoirs of maximum admitted mass
// 1024 fit in the 14-bit mass field.
export function ReservoirAddress(pixel: u32): u32 {
    return pixel * 14;
}

export function ReservoirLight(tag: u32): u32 {
    return tag & 255;
}

export function ReservoirMass(tag: u32): f32 {
    return Convert<f32>((tag >> 8) & 16383);
}

export function ReservoirQuota(firstTag: u32): u32 {
    return firstTag >> 22;
}

export function WriteReservoir(readwrite words: StorageBuffer<u32>, base: u32, slot: u32,
    light: u32, mass: f32, weight: f32, quota: u32): void {
    var tag: u32 = light | (U32(mass) << 8);
    if (slot == 0) {
        tag = tag | (quota << 22);
    }
    let address: u32 = base + slot * 2;
    words[address] = tag;
    words[address + 1] = Bitcast<u32>(weight);
}

export function ReservoirPoint(readonly words: StorageBuffer<u32>, base: u32): float3 {
    return float3(Bitcast<f32>(words[base + 8]), Bitcast<f32>(words[base + 9]), Bitcast<f32>(words[base + 10]));
}

export function ReservoirNormal(readonly words: StorageBuffer<u32>, base: u32): float3 {
    return float3(Bitcast<f32>(words[base + 11]), Bitcast<f32>(words[base + 12]), Bitcast<f32>(words[base + 13]));
}

export function WriteReservoirGeometry(readwrite words: StorageBuffer<u32>, base: u32, point: float3, normal: float3): void {
    words[base + 8] = Bitcast<u32>(point.x);
    words[base + 9] = Bitcast<u32>(point.y);
    words[base + 10] = Bitcast<u32>(point.z);
    words[base + 11] = Bitcast<u32>(normal.x);
    words[base + 12] = Bitcast<u32>(normal.y);
    words[base + 13] = Bitcast<u32>(normal.z);
}
