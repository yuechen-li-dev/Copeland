function produce(): number ! string {
  return ok(42);
}

function main(): number {
  const value: number = produce()!;
  return value;
}

function fallible(): number ! string {
  return produce()!;
}

function nested(): (number ! string) ! string {
  return ok(ok(7));
}

function unwrapNested(): number {
  return nested()!!;
}

enum Box {
  Value(result: number ! string),
}

function fromEnum(box: Box): number {
  return match box {
    Value(result) => result!,
  };
}

function saved(): void ! string {
  return;
}

function unwrapVoid(): void {
  saved()!;
}
