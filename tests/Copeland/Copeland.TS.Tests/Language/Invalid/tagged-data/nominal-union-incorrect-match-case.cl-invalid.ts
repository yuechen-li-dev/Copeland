record Circle {
  radius: number;
}

record Rectangle {
  width: number;
}

type Shape = Circle | Rectangle;

function bad(shape: Shape): number {
  return match shape {
    Circle(value) => value.radius,
    Triangle(value) => value.side,
  };
}
