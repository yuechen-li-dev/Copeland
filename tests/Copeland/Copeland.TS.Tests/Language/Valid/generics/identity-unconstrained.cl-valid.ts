function identity<T>(value: T): T {
    return value;
}

const answer: number = identity<number>(42);
