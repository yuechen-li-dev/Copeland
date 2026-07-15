record Circle {
  radius: number;
}

record Rectangle {
  width: number;
}

type Shape = Circle | Rectangle;

function bad(shape: Shape): Circle {
  return shape;
}
