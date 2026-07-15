interface Positioned {
  x: number;
  y: number;
}

record Point {
  x: number;
  y: number;
}

function identity<T>(value: T): T {
  return value;
}

function sum<T extends Positioned>(value: T): number {
  return value.x + value.y;
}

function main(): number {
  const point: Point = { x: 20, y: 22 };
  const explicit: number = identity<number>(sum<Point>(point));
  const inferred: number = identity(sum(point));
  return explicit + inferred;
}
