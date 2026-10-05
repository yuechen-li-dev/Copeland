using System.Collections.Generic;

function countValues(values: string[]): System.Collections.Generic.Dictionary<string, int> {
    const counts = new Dictionary<string, int>();

    for (const value of values) {
        let previous: int = 0;
        if (counts.ContainsKey(value)) {
            previous = counts[value];
        }
        counts[value] = previous + 1;
    }

    return counts;
}
