record Circle {
  radius: number;
}

interface Required {
  value: number;
}

type Shape = Required | Circle;
