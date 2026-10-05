function transformBatch(values: int[]): int[] {
    return batch values as item {
        return item < 0 ? -item : item * 2;
    };
}