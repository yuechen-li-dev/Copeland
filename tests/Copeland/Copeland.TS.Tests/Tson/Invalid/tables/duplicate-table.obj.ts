// expected: COPE-TSON-TABLE-0002
const $schema: string = "copeland://fixtures/table/invalid";
record table Values { x: [1]; }
record table Values { x: [2]; }
const $value = Values;
