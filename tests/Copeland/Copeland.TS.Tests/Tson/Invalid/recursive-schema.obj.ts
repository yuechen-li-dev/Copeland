// expected: COPE-TSON-0003
const $schema: string = "copeland://fixtures/invalid";
record Node { next: Node; }
const $value = true;
