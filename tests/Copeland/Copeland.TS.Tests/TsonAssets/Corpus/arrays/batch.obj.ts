// ARRAY-M0b authoring asset: comments and field order normalize in canonical TSON.
const $schema: string = "copeland://fixtures/assets-arrays";
record Entry { label: string; }
enum State { Off, On(value: number), }
record Batch { empty: number[]; values: number[]; items: Entry[]; states: State[]; rows: number[][]; }
const $value: Batch = {
    rows: [[], [1, 2]],
    states: [State.Off, State.On($number("4008000000000000"))],
    items: [{ label: "first" }, { label: "second" }],
    values: [$number("0000000000000000"), $number("8000000000000000"), 3],
    empty: [],
};
