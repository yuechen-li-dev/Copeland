import { KernelParameters, MaskedScore } from "./KernelMath";

export function AttentionScore(dot: f32, query: u32, key: u32, keys: u32): f32 {
    return MaskedScore<false, 0.125>(dot, query, key, keys);
}

export function ValueRowOffset(p: KernelParameters): u32 {
    return p.columns;
}
