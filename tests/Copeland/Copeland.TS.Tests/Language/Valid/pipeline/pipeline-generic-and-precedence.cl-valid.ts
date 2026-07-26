record Point {
    x: number;
    y: number;
}

function identity<T>(value: T): T { return value; }
function increment(value: number): number { return value + 1; }
function normalize(point: Point): Point { return point; }
function read(): number ! string { return ok(1); }

function resultValue(): number ! string {
    return read()? |> increment;
}

function main(): number {
    const original: Point = { x: 1, y: 2 };
    const point: Point = original with { x: 3 } |> normalize;
    const value: int = 1 |> identity;
    return point.x;
}
