enum Inner {
  None,
  Number(value: number),
}

enum Outer {
  Empty,
  Single(value: number),
  Pair(first: number, second: string),
  Nested(value: Inner),
}

function main(): string {
  const outer: Outer = Outer.Nested(Inner.Number(9));
  return match outer {
    Empty => "empty",
    Single(value) => "single",
    Pair(first, second) => second,
    Nested(inner) => match inner {
      None => "none",
      Number(value) => "nested",
    },
  };
}
