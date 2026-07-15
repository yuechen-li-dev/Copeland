interface Positioned {
    x: number;
    y: number;
}

record Point {
    x: number;
    y: number;
    label: string;
}

type PointAlias = Point;

function sum<T extends Positioned>(value: T): number {
    return value.x + value.y;
}

const point: PointAlias = { x: 20, y: 22, label: "ok" };
const answer: number = sum(point);
