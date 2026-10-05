function countValues(values: string[]): Map<string, int> {
    const counts: Map<string, int> = new Map();
    for (const value of values) {
        const previous = get(counts, value) ?? 0;
        set(counts, value, previous + 1);
    }
    return counts;
}