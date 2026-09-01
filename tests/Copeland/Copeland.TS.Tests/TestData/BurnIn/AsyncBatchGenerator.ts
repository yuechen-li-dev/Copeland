record Sample {
    id: int;
    value: number;
}

class LoadedValue {
    public raw: number;
    private scaled: number;

    constructor(raw: number, scaled: number): LoadedValue {
        return { raw, scaled };
    }

    total(value: LoadedValue): number {
        return value.raw + value.scaled;
    }
}

function normalize(value: number): number {
    return value * 2;
}

function validate(value: number): number ! string {
    if (value < 0) {
        return err("negative");
    }
    return ok(value);
}

function* seedValues(): Iterable<number> {
    yield 1;
    yield return 2;
    yield return 3;
}

function* values(): Iterable<number> {
    yield 0;
    yield* seedValues();
    yield return 4;
}

function collect(): number[] {
    const buffer: MutableArray<number> = MutableArray<number>(5);
    let index: int = 0;
    for (const value of values()) {
        buffer[index] = value;
        index = index + 1;
    }
    return buffer.freeze();
}

function transform(input: number[]): number[] {
    const increment: number = 1;
    return batch input as value {
        const checked: number = validate(value)!;
        return normalize(checked + increment);
    };
}

async function load(value: number): number ! string {
    const checked: number = validate(value)?;
    return checked + 1;
}

async function compose(value: number): number ! string {
    const pending: Async<number ! string> = load(value);
    const loaded: number = await pending?;
    const boxed: LoadedValue = LoadedValue(loaded, loaded * 2);
    const local = { value: loaded, doubled: loaded * 2 };
    return LoadedValue.total(boxed) + local.value + local.doubled;
}

function main(): number {
    const collected: number[] = collect();
    const transformed: number[] = transform(collected);
    let total: number = 0;
    for (const value of transformed) {
        total = total + value;
    }
    return total;
}
