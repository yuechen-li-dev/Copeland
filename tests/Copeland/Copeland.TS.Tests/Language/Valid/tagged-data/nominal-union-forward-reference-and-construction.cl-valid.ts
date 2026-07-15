type Shape = Circle | Rectangle;

record Circle {
  radius: number;
}

record Rectangle {
  width: number;
  height: number;
}

function makeCircle(): Shape {
  return Shape.Circle({ radius: 4 });
}

function makeRectangle(): Shape {
  return Shape.Rectangle({ width: 3, height: 5 });
}

function total(): number {
  return match makeCircle() {
    Circle(value) => value.radius,
    Rectangle(value) => value.width + value.height,
  };
}
