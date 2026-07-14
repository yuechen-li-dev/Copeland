// expected: COPE-TSON-TABLE-0002
const $schema: string = "copeland://fixtures/table/invalid";
record Wrapper { nested: Values; }
record table Values { x: [1]; }
const $value = Values;
