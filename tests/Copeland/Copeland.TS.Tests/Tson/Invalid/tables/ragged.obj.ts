// expected: COPE-TSON-TABLE-0003
const $schema: string = "copeland://fixtures/table/invalid";
record table Values { x: [1, 2]; y: [3]; }
const $value = Values;
