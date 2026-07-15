record Circle {
  radius: number;
}

record Rectangle {
  width: number;
}

type Round = Circle;
type Shape = Round | Rectangle;
