function f(): number ! ParseError {
  return 1;
}

function g(): number ! AppError {
  const x: number = f()?;
  return x;
}
