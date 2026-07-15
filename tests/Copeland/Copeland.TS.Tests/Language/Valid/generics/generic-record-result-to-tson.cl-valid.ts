const $schema: string = "copeland://tests/generic-record";

interface Positioned {
    x: number;
    y: number;
}

record Point {
    x: number;
    y: number;
}

function clonePoint<T extends Positioned>(value: T): Point {
    return { x: value.x, y: value.y };
}

function encode(): string ! TsonEncodeError {
    return tsonEncode(clonePoint<Point>({ x: 1, y: 2 }));
}
