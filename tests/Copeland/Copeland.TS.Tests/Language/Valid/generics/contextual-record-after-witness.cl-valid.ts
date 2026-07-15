record Point {
  x: number;
  y: number;
}

function combinePoint<T>(witness: T, value: T): T {
  return value;
}

const point: Point = { x: 20, y: 22 };
const answer: Point = combinePoint(point, { x: 1, y: 2 });
