using System.Collections.Generic;

function collectEvenValues(values: int[]): int[] {
    const result = new List<int>();

    for (const value of values) {
        if (value % 2 === 0) {
            result.Add(value);
        }
    }

    return result.ToArray();
}
