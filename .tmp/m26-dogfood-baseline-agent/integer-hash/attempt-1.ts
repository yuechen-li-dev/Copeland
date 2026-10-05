import { Math } from "System";
function hashIntegers(values: int[]): int {
    let hash: int = -2128831035;
    for (const value of values) {
        hash = Math.imul(xor(hash, value), 16777619);
    }
    return hash;
}