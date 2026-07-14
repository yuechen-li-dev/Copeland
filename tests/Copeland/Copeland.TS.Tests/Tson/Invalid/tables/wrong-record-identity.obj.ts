// expected: COPE-TSON-TABLE-0004
const $schema: string = "copeland://fixtures/table/invalid";
record First { value: number; }
record Second { value: number; }
record table Values { item: First = [$record.Second({ "value": 1 })]; }
const $value = Values;
