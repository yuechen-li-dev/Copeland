// expected: COPE-TSON-TABLE-0001
const $schema: string = "copeland://fixtures/table/invalid";
record table First { x: [1]; }
record table Second { x: [2]; }
const $value = First;
