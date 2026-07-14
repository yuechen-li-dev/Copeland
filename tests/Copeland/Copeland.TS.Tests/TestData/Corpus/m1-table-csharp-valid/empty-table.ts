record table Empty {
    value: number = [];
}

function main(): number {
    return match Empty[0] {
        ok(row) => 1,
        err(error) => match error {
            InvalidIndex(index) => 2,
            OutOfBounds(index, rowCount) => rowCount,
        },
    };
}
