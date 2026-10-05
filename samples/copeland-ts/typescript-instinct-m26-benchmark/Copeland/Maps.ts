using System.Collections.Generic;

export function runMaps(rounds: int, count: int): int {
    let checksum: int = 0;
    for (let round: int = 0; round < rounds; round++) {
        const values: Dictionary<string, int> = new Dictionary<string, int>();
        for (let i: int = 0; i < count; i++) {
            values[`key${i}`] = i;
        }
        for (let i: int = 0; i < count; i++) {
            const key: string = `key${i}`;
            values[key] = values[key] + 1;
            checksum += values[key];
        }
        for (let i: int = 0; i < count; i++) {
            if (i % 2 == 0) {
                values.Remove(`key${i}`);
            }
        }
        checksum += values.Count;
    }
    return checksum;
}
