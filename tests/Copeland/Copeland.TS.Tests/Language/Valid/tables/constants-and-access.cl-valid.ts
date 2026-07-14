record Point {
    x: number;
}

enum State {
    Empty,
    Value(value: number),
}

record table First {
    Row: number = [1];
    length: string = ["one"];
    count: boolean = [true];
    rows: Point = [{ x: 2 }];
    columns: State = [State.Value(3)];
    at: number ! string = [ok(4)];
}

record table Second {
    Row: number = [5];
    length: string = ["two"];
    count: boolean = [false];
    rows: Point = [{ x: 6 }];
    columns: State = [State.Empty];
    at: number ! string = [err("bad")];
}

function singleton(value: First): First {
    return value;
}

function row(index: number): First.Row ! TableBoundsError {
    return First[index];
}

function readColumn(index: number): number ! TableBoundsError {
    return First.Row[index];
}

function staticallyInvalidIndex(): First.Row ! TableBoundsError {
    return First[-1];
}

function readField(index: number): number ! TableBoundsError {
    const item: First.Row = First[index]?;
    return ok(item.Row);
}

function combine(source: First, row: First.Row, values: column number): void {
    return;
}
