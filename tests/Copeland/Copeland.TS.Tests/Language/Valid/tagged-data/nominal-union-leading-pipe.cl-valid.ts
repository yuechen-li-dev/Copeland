record Circle {
  radius: number;
}

record Rectangle {
  width: number;
  height: number;
}

type Shape =
  | Circle
  | Rectangle;

function area(shape: Shape): number {
  return match shape {
    Circle(value) => value.radius * value.radius,
    Rectangle(value) => value.width * value.height,
  };
}
