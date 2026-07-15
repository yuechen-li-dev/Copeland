record Circle {
  radius: number;
}

record Rectangle {
  width: number;
  height: number;
}

type Shape = Circle | Rectangle;

function area(): number {
  const circle: Circle = { radius: 4 };
  const shape: Shape = circle;
  return match shape {
    Circle(value) => value.radius * value.radius,
    Rectangle(value) => value.width * value.height,
  };
}
