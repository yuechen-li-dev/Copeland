function parseNumber(text: string): number ! ParseError {
  return 1;
}

function value(): number ! AppError {
  return parseNumber("1")?;
}
