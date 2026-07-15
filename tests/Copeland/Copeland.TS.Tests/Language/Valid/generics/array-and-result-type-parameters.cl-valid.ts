function relayArray<T>(value: T[]): T[] {
    return value;
}

function relayResult<T, E>(value: T ! E): T ! E {
    return value;
}

const values: number[] = relayArray<number>([1, 2]);
const result: number ! string = relayResult<number, string>(ok(1));
