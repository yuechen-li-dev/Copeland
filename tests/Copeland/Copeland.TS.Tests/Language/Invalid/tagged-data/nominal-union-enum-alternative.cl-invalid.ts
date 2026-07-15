record Circle {
  radius: number;
}

enum Existing {
  Value,
}

type Shape = Existing | Circle;
