type Adder = (value: int) => int;

function makeAdder(k: int): Adder {
    return capture { k } (value: int) => value + k;
}

export function runClosures(iterations: int): int {
    const adders: Adder[] = [
        makeAdder(0), makeAdder(1), makeAdder(2), makeAdder(3),
        makeAdder(4), makeAdder(5), makeAdder(6), makeAdder(7),
        makeAdder(8), makeAdder(9), makeAdder(10), makeAdder(11),
        makeAdder(12), makeAdder(13), makeAdder(14), makeAdder(15)
    ];
    let acc: int = 0;
    for (let i: int = 0; i < iterations; i = i + 1) {
        const add: Adder = adders[i % 16];
        acc = add(acc) % 1000003;
    }
    return acc;
}
