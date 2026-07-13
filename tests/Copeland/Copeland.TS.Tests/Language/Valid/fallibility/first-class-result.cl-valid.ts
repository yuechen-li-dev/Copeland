enum Box {
  Value(outcome: number ! string),
}

function produce(): number ! string {
  return ok(1);
}

function fail(): number ! string {
  return err("failed");
}

function inspect(outcome: number ! string): number {
  return match outcome {
    ok(value) => value,
    err(error) => 0,
  };
}

function forward(outcome: number ! string): number ! string {
  return outcome;
}

function stored(): number ! string {
  const outcome: number ! string = produce();
  const value: number = outcome?;
  return value;
}

function boxed(): Box {
  return Box.Value(produce());
}

function nested(): (number ! string) ! string {
  const outcome: (number ! string) ! string = ok(ok(1));
  return outcome;
}

function save(): void ! string {
  return;
}
