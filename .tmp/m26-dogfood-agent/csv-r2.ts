function parseCsvItem(part: string): int {
    return parseInt(part.trim(), 10);
}

function parseCsvIntegers(text: string): int[] {
    const parts = String.Split(text, ",");
    return batch parts as part {
        return parseCsvItem(part);
    };
}
