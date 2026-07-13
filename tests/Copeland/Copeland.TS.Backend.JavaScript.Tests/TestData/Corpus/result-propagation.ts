function good(): number ! string {
  return ok(4);
}

function stored(): number ! string {
  const outcome: number ! string = good();
  const value: number = outcome?;
  return value + 1;
}
