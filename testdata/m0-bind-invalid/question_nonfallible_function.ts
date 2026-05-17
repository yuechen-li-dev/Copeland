function f(): number ! ParseError {
  return 1;
}

function g(): number {
  const x: number = f()?;
  return x;
}
