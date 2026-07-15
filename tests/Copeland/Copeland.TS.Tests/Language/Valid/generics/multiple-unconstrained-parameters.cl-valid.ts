function chooseLeft<T, U>(left: T, right: U): T {
    return left;
}

const answer: string = chooseLeft<string, number>("value", 42);
