const $schema: string = "copeland://fixtures/nominal-union";

record Circle {
  radius: number;
}

record Rectangle {
  width: number;
  height: number;
}

type Shape = Circle | Rectangle;

function encode(): string ! TsonEncodeError {
  const circle: Circle = { radius: 4 };
  const shape: Shape = circle;
  return tsonEncode(shape);
}
