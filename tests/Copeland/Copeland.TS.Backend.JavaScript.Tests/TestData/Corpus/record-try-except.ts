record Point {
  x: number;
  y: number;
}

function bad(): Point ! string { return err("bad"); }
function fallback(): Point { return { x: 40, y: 2 }; }

function main(): number {
  const point: Point = try { bad()? } except (error) { fallback() };
  return point.x + point.y;
}
