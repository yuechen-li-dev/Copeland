// expected: COPE-TSON-TABLE-0003
const $schema: string = "copeland://fixtures/table/invalid";
record table Values { x: [1]; x: [2]; }
const $value = Values;
