function relay<T, E>(value: T ! E): T ! E {
  return value;
}

const source: number ! string = ok(42);
const value: number ! string = relay(source);
