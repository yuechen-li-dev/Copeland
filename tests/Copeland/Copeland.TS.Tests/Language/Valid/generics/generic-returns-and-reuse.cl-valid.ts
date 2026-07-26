function identity<T>(value: T): T {
    return value;
}

function chooseLeft<T, U>(left: T, right: U): T {
    return left;
}

const first: number = identity<number>(1);
const second: int = identity(2);
const chosen: int = chooseLeft(second, "skip");
