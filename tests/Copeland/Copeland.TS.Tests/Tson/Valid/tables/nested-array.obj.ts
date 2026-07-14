const $schema: string = "copeland://fixtures/table/nested-array";
record table Matrices { values: number[][] = [[[1, 2], []], [[3], [4]]]; }
const $value = Matrices;
