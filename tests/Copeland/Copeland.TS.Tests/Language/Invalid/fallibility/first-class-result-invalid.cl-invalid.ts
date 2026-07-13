function plain(): number {
  ok(1);
  err("no");
  return 1;
}

function wrongOk(): number ! string {
  return ok("wrong");
}

function wrongErr(): number ! string {
  return err(1);
}

function missingArm(outcome: number ! string): number {
  return match outcome {
    ok(value) => value,
  };
}

function duplicateArm(outcome: number ! string): number {
  return match outcome {
    ok(value) => value,
    ok(other) => other,
    err(error) => 0,
  };
}

function discarded(): number {
  produce();
  return 1;
}

function malformed(): number ! string ! string {
  return 1;
}

function produce(): number ! string {
  return 1;
}
