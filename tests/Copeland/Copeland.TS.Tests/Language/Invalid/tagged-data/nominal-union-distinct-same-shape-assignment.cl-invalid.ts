record Circle {
  radius: number;
}

record Rectangle {
  width: number;
}

type Shape = Circle | Rectangle;
type OtherShape = Circle | Rectangle;

function bad(shape: Shape): OtherShape {
  return shape;
}
