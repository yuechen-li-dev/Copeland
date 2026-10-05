function hashIntegers(values: int[]): int {
    let hash: int = 0x811c9dc5 | 0;

    for (const value of values) {
        hash = ((hash ^ value) * (0x01000193)) | 0;
    }

    return hash;
}
