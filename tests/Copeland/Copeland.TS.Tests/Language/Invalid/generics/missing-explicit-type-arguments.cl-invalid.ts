function identity<T>(value: T): T {
  return value;
}

const value: number = identity(42);
