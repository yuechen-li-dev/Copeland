function same<T>(left: T, right: T): T {
  return left;
}

const value: number = same(1, "two");
