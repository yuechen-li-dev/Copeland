record Point {
  x: number;
  y: number;
}

function load(): Point ! string {
  return ok({ x: 40, y: 2 });
}

function main(): number {
  const point: Point = load()!;
  return point.x + point.y;
}
