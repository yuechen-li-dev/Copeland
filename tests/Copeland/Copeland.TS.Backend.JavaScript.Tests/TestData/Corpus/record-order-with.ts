record Point {
  x: number;
  y: number;
}

function first(): number { return 40; }
function second(): number { return 2; }

function main(): number {
  let point: Point = { y: second(), x: first() };
  point = point with { y: second(), x: first() };
  return point.x + point.y;
}
