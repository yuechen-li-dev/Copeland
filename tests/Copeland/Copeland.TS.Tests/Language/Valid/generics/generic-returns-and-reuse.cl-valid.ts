function identity<T>(value: T): T {
    return value;
}

function chooseLeft<T, U>(left: T, right: U): T {
    return left;
}

const first: number = identity<number>(1);
const second: number = identity(2);
const chosen: number = chooseLeft(second, "skip");
