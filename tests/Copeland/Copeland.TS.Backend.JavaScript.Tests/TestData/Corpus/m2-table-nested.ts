record Point {
    x: number;
}

enum State {
    Empty,
    Value(point: Point),
}

record table Values {
    point: Point = [{ x: 1 }, { x: 2 }];
    state: State = [State.Value({ x: 3 }), State.Empty];
    result: State ! string = [ok(State.Value({ x: 4 })), err("bad")];
}

function main(): number {
    const row: Values.Row = Values[1]!;
    return row.point.x + match Values.result[0]! {
        ok(value) => match value { Value(point) => point.x, Empty => 0, },
        err(error) => 0,
    };
}
