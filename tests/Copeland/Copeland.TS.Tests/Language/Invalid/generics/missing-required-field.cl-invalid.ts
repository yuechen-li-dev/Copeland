interface Positioned {
  x: number;
  y: number;
}

record PartialPoint {
  x: number;
}

function sum<T extends Positioned>(value: T): number {
  return value.x + value.y;
}

const value: number = sum<PartialPoint>({ x: 1 });
