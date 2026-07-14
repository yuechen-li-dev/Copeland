record Point {
  x: number;
  y: number;
}

function main(): number {
  const point: Point = { x: 40, y: 2 };
  return point.x + point.y;
}
