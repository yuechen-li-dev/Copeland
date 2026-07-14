const $schema: string = "copeland://fixtures/table/primitive";
// Authoring form permits comments and inferred nonempty primitive columns.
record table Samples {
    enabled: [true, false];
    score: [1, -0];
    label: ["first", "second"];
}
const $value = Samples;
