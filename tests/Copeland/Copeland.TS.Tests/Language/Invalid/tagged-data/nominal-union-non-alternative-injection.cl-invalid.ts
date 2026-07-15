record Circle {
  radius: number;
}

record Rectangle {
  width: number;
}

record Triangle {
  side: number;
}

type Shape = Circle | Rectangle;

function bad(): Shape {
  const triangle: Triangle = { side: 3 };
  return triangle;
}
