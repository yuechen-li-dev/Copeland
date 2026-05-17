function parseNumber(text: string): number ! ParseError {
  return 1;
}

function caller(text: string): number ! ParseError {
  const x: number = parseNumber(text)?;
  return x;
}
