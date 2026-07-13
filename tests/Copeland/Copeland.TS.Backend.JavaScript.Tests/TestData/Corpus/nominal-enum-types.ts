enum First {
  Value(value: number),
}

enum Second {
  Value(value: number),
}

function first(): First {
  return First.Value(1);
}

function second(): Second {
  return Second.Value(2);
}

function main(): number {
  return match second() {
    Value(value) => value,
  };
}
