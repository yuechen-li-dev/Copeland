// ARRAY-M1 authoring source: declaration and element order are intentional.
const $schema: string = "copeland://corpus/runtime-array-encoding";
record Detail { label: string; }
enum Signal { Idle, Text(value: string), DetailValue(detail: Detail), }
record Packet {
    emptyNumbers: number[];
    booleans: boolean[];
    numbers: number[];
    texts: string[];
    nested: number[][];
    details: Detail[];
    signals: Signal[];
    emptyDetails: Detail[];
}

const $value: Packet = {
    signals: [Signal.Idle, Signal.Text("payload"), Signal.DetailValue({ label: "nested record" })],
    details: [{ label: "first" }, { label: "second" }],
    nested: [[$number("0000000000000000"), $number("8000000000000000")], [$number("3FF8000000000000")], []],
    texts: ["quote: \"; slash: \\; line: \n", "snow 雪 😀"],
    numbers: [$number("0000000000000000"), $number("8000000000000000"), $number("3FF8000000000000"), $number("7FF8000000000000"), $number("7FF0000000000000"), $number("FFF0000000000000")],
    booleans: [true, false, true],
    emptyDetails: [],
    emptyNumbers: [],
};
