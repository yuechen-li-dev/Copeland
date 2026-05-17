enum Shape {
  Circle(radius: number),
  Rect(width: number, height: number),
}

function make(): Shape {
  const c: Shape = Shape.Circle(10);
  return Shape.Rect(3, 4);
}
