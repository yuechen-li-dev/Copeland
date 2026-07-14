// expected: COPE-TSON-0004
const $schema: string = "copeland://fixtures/invalid";
record Left { value: number; }
record Right { value: number; }
const $value: Right = $record.Left({ value: 1 });
