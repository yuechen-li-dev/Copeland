record Circle {
  radius: number;
}

record Rectangle {
  width: number;
}

type Inner = Circle | Rectangle;
type Outer = Inner | Circle;
