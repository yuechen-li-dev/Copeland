record Point {
  x: number;
  y: number;
}

function main(): number {
  const source: Point = { x: 1, y: 2 };
  const updated: Point = source with { y: 2, x: 40 };
  return updated.x + updated.y;
}
