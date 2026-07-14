record table Empty {
    value: number = [];
}

record table Values {
    x: [-0, 2];
    label: string = ["zero", "two"];
}

function main(): number {
    const row: Values.Row = Values[1]!;
    return row.x;
}

function bounds(index: number): number {
    return match Values.x[index] {
        ok(value) => value,
        err(error) => match error {
            InvalidIndex(value) => 10,
            OutOfBounds(value, rowCount) => rowCount,
        },
    };
}
