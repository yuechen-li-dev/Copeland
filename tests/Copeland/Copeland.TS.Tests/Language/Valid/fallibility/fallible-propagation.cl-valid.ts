function parseNumber(text: string): number ! ParseError {
  return 1;
}

function caller(text: string): number ! ParseError {
  const value: number = parseNumber(text)?;
  return value;
}
