function Run(iterations: int): int {
    let checksum: int = 0;

    for (let index: int = 0; index < iterations; index = index + 1) {
        const group: int = index % 97;
        const message: string = `item:${String.From(group)}; state:${String.From(index % 5)}; label:copeland`;
        const expected: string = `item:${String.From(group)}; state:${String.From(index % 5)}; label:copeland`;
        if (message == expected) {
            checksum = checksum + group + 1;
        }
    }

    return checksum;
}
