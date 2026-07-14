const $schema: string = "copeland://fixtures/empty";

record EmptyRecord {
}

enum State {
    Ready,
}

const $value = {
    object: {},
    "record": $record.EmptyRecord({}),
    state: State.Ready,
};
