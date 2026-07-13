function good(): number ! string {
  return ok(4);
}

function bad(): number ! string {
  return err("bad");
}

function inspect(value: number ! string): number {
  return match value {
    ok(numberValue) => numberValue,
    err(error) => 0,
  };
}
