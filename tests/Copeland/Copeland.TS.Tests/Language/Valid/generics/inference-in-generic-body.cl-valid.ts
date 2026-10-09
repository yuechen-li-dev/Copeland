function inner<T>(value: T): T {
  return value;
}

function outer<U>(value: U): U {
  return inner(value);
}
