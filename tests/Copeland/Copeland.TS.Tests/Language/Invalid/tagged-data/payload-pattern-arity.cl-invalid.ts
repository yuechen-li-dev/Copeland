enum Shape {
  Rect(width: number, height: number),
}

function area(shape: Shape): number {
  return match shape {
    Rect(width) => width,
  };
}
