record Point {
  x: number;
  y: number;
}

enum Event {
  Moved(point: Point),
}

function load(): Point ! string { return ok({ x: 40, y: 2 }); }

function main(): number {
  const event: Event = Event.Moved(load()!);
  return match event {
    Moved(point) => point.x + point.y,
  };
}
