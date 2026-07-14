const $schema: string = "copeland://fixtures/assets-arrays";

record Entry {
    label: string;
}

enum State {
    Off,
    On(value: number),
}

record Batch {
    empty: number[];
    values: number[];
    items: Entry[];
    states: State[];
    rows: number[][];
}

function load(): Batch {
    const batch: Batch = tsonAsset("./batch.obj.ts");
    return batch;
}
