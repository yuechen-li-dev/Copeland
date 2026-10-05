using Map = System.Collections.Generic.Dictionary;
function countValues(values: string[]): Map<string, int> {
    const counts: Map<string, int> = new Map();
    for (const value of values) {
        const previous: int = counts.get(value) ?? 0;
        counts.set(value, previous + 1);
    }
    return counts;
}