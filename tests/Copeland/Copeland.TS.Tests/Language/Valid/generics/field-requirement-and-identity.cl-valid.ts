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

const point: Point = { x: 20, y: 22 };
const answer: number = sum<Point>(point);
const same: number = identity<number>(answer);
