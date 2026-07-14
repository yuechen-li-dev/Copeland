const $schema: string = "copeland://fixtures/nominal";

record Label {
    text: string;
}

record AlternateLabel {
    text: string;
}

enum Choice {
    Empty,
    Labelled(label: Label),
}

const $value = {
    authoredOrder: { z: false, a: true },
    label: $record.Label({ text: "primary" }),
    alternate: $record.AlternateLabel({ text: "secondary" }),
    choice: Choice.Labelled($record.Label({ text: "nested" })),
};
