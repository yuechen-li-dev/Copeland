record Point {
    x: number;
    y: number;
}

enum ReadingState {
    Missing,
    Present(value: number),
    Flagged(reason: string),
}

record table Readings {
    station: string = ["north", "south", "east", "west"];
    sample: int = [1, 2, 3, 4];
    point: Point = [
        { x: 1, y: 2 },
        { x: 3, y: 4 },
        { x: 5, y: 6 },
        { x: 7, y: 8 }
    ];
    state: ReadingState = [
        ReadingState.Present(10),
        ReadingState.Present(20),
        ReadingState.Missing,
        ReadingState.Flagged("sensor")
    ];
    accepted: number ! string = [
        ok(10),
        ok(20),
        err("missing"),
        err("flagged")
    ];
}

function stateValue(value: ReadingState): number {
    return match value {
        Missing => 0,
        Present(reading) => reading,
        Flagged(reason) => Float.From(reason.length),
    };
}

function acceptedValue(value: number ! string): number {
    return match value {
        ok(reading) => reading,
        err(error) => Float.From(0),
    };
}

function rowScore(index: int): number ! TableBoundsError {
    const row: Readings.Row = Readings[index]?;
    const pointScore: number = row.point.x + row.point.y;
    return ok(
        Float.From(row.sample)
        + pointScore
        + stateValue(row.state)
        + acceptedValue(row.accepted)
    );
}

function columnScore(): number {
    let index: int = 0;
    let total: number = 0;
    while (index < 4) {
        total = total
            + Float.From(Readings.sample[index]!)
            + Readings.point[index]!.x;
        index = index + 1;
    }
    return total;
}

function boundsScore(index: int): number {
    return match Readings.sample[index] {
        ok(value) => Float.From(value),
        err(error) => match error {
            InvalidIndex(value) => Float.From(100),
            OutOfBounds(value, rowCount) => rowCount,
        },
    };
}

function repeatedAccess(): number {
    const first: Readings.Row = Readings[0]!;
    const second: Readings.Row = Readings[1]!;
    const third: Readings.Row = Readings[2]!;
    const fourth: Readings.Row = Readings[3]!;
    return first.point.x
        + second.point.x
        + third.point.x
        + fourth.point.x
        + stateValue(first.state)
        + stateValue(second.state)
        + stateValue(third.state)
        + stateValue(fourth.state);
}

function main(): number {
    return rowScore(0)!
        + rowScore(1)!
        + rowScore(2)!
        + rowScore(3)!
        + columnScore()
        + boundsScore(99)
        + repeatedAccess();
}
