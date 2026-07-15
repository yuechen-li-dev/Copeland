record Shape {
  value: number;
}

record Circle {
  radius: number;
}

record Rectangle {
  width: number;
}

type Shape = Circle | Rectangle;
