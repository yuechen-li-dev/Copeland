enum Shape {
  Point,
  Circle(radius: number),
  Rect(width: number, height: number),
}

function area(shape: Shape): number {
  return match shape {
    Point => 0,
    Circle(radius) => radius,
    Rect(width, height) => width * height,
  };
}
