export function runNativeStrings(rounds: int, count: int): int {
    let total: int = 0;
    for (let round: int = 0; round < rounds; round++) {
        const parts: MutableArray<string> = MutableArray<string>(count, "");
        for (let i: int = 0; i < count; i++) {
            parts[i] = `item${i}`;
        }
        const joined: string = String.Join(parts.freeze(), ",");
        const back: string[] = String.Split(joined, ",");
        let hash: int = 0;
        for (let i: int = 0; i < joined.length; i++) {
            hash = hash * 31 + String.CodeAt(joined, i);
        }
        total += back.length + hash % 256 + String.IndexOf(joined, "item99999");
    }
    return total;
}
