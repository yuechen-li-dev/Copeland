record Point {
  x: number;
  y: number;
}

enum Event {
  Moved(point: Point),
}

function main(): number {
  const event: Event = Event.Moved({ x: 40, y: 2 });
  return match event {
    Moved(point) => point.x + point.y,
  };
}
