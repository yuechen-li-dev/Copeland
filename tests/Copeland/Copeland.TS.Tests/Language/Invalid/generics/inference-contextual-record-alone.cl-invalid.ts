record Point {
  x: number;
  y: number;
}

function identity<T>(value: T): T {
  return value;
}

const value: Point = identity({ x: 1, y: 2 });
