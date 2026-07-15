record Circle {
  radius: number;
}

record Rectangle {
  width: number;
  height: number;
}

type Shape = Circle | Rectangle;

record Holder {
  shape: Shape;
}

enum Envelope {
  Value(shape: Shape),
}

function identity<T>(value: T): T {
  return value;
}

function accept(shape: Shape): Shape {
  return shape;
}

function choose(flag: boolean, first: Circle, second: Rectangle): Shape {
  return if flag { first } else { second };
}

function wrap(flag: boolean, circle: Circle, rectangle: Rectangle): Shape {
  return if flag { circle } else { rectangle };
}

function build(): number {
  const circle: Circle = { radius: 4 };
  const rectangle: Rectangle = { width: 3, height: 5 };
  let assigned: Shape = circle;
  assigned = rectangle;
  const returned: Shape = accept(circle);
  const explicitGeneric: Shape = identity<Shape>(circle);
  const inferredGeneric: Circle = identity(circle);
  const holder: Holder = { shape: circle };
  const envelope: Envelope = Envelope.Value(circle);
  const values: Shape[] = [circle, rectangle];
  const result: Shape ! string = ok(circle);
  const conditional: Shape = choose(true, circle, rectangle);
  const matched: Shape = wrap(false, circle, rectangle);
  const fromResult: Shape = result!;
  return match matched {
    Circle(value) => value.radius + inferredGeneric.radius,
    Rectangle(value) => value.width + match fromResult {
      Circle(inner) => inner.radius,
      Rectangle(inner) => inner.width,
    },
  };
}
