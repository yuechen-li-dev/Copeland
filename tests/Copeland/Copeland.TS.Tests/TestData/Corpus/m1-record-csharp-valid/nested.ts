record Point {
  x: number;
  y: number;
}

record Envelope {
  point: Point;
}

function main(): number {
  const envelope: Envelope = { point: { x: 40, y: 2 } };
  return envelope.point.x + envelope.point.y;
}
