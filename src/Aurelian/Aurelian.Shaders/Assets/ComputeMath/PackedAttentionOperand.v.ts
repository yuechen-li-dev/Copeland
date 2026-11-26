import { ReadOperand } from "./Float16";

export function Operand(readonly data: StorageBuffer<u32>, index: u32): f32 {
    return ReadOperand<true>(data, index);
}
