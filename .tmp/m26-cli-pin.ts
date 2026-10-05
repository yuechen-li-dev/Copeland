interface Positioned {
    x: number;
    y: number;
}

record Point {
    x: number;
    y: number;
}

function sum<T extends Positioned>(value: T): number {
    return value.x + value.y;
}

function identity<T>(value: T): T {
    return value;
}

function mainSum(): number {
    const point: Point = { x: 20, y: 22 };
    return sum(point);
}

function mainIdentity(): number {
    return identity<number>(42);
}
