function f(): number ! ParseError {
  return 1;
}

function g(): number {
  return f() + 1;
}
