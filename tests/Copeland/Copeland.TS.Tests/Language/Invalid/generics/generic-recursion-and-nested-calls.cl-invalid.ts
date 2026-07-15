function identity<T>(value: T): T {
    return value;
}

function recursive<T>(value: T): T {
    return recursive<T>(value);
}

function outer<T>(value: T): T {
    return identity<T>(value);
}
