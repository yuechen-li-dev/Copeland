// expected: COPE-TSON-0004
const $schema: string = "copeland://fixtures/invalid";
enum Left { Same, }
enum Right { Same, }
const $value: Right = Left.Same;
