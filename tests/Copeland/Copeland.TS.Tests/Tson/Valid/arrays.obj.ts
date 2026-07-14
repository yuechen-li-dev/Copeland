const $schema: string = "copeland://fixtures/arrays";

record Entry {
    label: string;
}

enum State {
    Off,
    On(value: number),
}

record Batch {
    flags: boolean[];
    names: string[];
    entries: Entry[];
    states: State[];
    rows: number[][];
}

const $value: Batch = {
    rows: [[], [1, 2]],
    states: [State.Off, State.On(2)],
    entries: [{ label: "first" }, { label: "second" }],
    names: [],
    flags: [true, false],
};
