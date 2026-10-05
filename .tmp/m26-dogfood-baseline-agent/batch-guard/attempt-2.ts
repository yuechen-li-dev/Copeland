function transformBatch(values: int[]): int[] {
    return batch values as item {
        return if (item < 0) { -item } else { item * 2 };
    };
}