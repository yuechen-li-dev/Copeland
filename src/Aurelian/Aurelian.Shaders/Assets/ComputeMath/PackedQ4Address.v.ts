// Scale words precede four nibble planes. An interleaved plane holds eight adjacent outputs.
export function Q4BlockAddress(row: u32, inputIndex: u32, inputWidth: u32, rowsPerBlock: u32, scaleWords: u32): u32 {
    let block = (row / rowsPerBlock) * (inputWidth / 32) + inputIndex / 32;
    return block * (scaleWords + 4 * rowsPerBlock);
}

export function Q4Scale(readonly weights: StorageBuffer<u32>, address: u32, rowInBlock: u32,
    rowsPerBlock: u32, scaleWords: u32): f32 {
    if (scaleWords == rowsPerBlock) {
        return Bitcast<f32>(weights[address + rowInBlock]);
    }
    let pair = weights[address + rowInBlock / 2];
    return UnpackFloat16(pair >> (rowInBlock % 2 * 16));
}

function Q4Codes(bits: u32): float4 {
    return float4(Convert<f32>(bits & 15), Convert<f32>((bits >> 4) & 15),
        Convert<f32>((bits >> 8) & 15), Convert<f32>((bits >> 12) & 15));
}

export function Q4UnsignedDotWord(word: u32, first: float4, second: float4): f32 {
    return Dot(Q4Codes(word), first) + Dot(Q4Codes(word >> 16), second);
}
