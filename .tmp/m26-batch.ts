function expensive(value: int): int {
    let result: int = value;
    for (let i: int = 0; i < 1000; i = i + 1) {
        result = result * 31 + i;
    }
    return result;
}
function cheap(input: int[]): int[] {
    return batch input as item { return item * 2; };
}
function costly(input: int[]): int[] {
    return batch input as item { return expensive(item); };
}
