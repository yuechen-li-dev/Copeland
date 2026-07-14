const $schema: string = "copeland://corpus/tson-table-m1";

record Point {
    x: number;
    label: string;
}

enum State {
    Missing,
    Named(label: string),
}

record table Samples from tsonAsset("./samples.obj.ts") {
    active: boolean;
    score: number;
    label: string;
    point: Point;
    state: State;
    values: number[][];
}

record table Empty from tsonAsset("./empty.tson") {
    value: number;
}

function observation(): string {
    const row: Samples.Row = Samples[1]!;
    return match row.state {
        Missing => "missing",
        Named(label) => label,
    };
}

function negativeZero(): number {
    return Samples.score[0]!;
}

function nested(): number[][] {
    return Samples.values[1]!;
}

function emptyBounds(): number {
    return match Empty.value[0] {
        ok(value) => value,
        err(error) => match error {
            InvalidIndex(index) => 1000,
            OutOfBounds(index, rowCount) => 2000,
        },
    };
}
