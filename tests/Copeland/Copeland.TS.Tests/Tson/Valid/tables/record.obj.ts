const $schema: string = "copeland://fixtures/table/record";
record Point { x: number; y: number; }
record table Points { point: Point = [{ x: 1, y: 2 }]; }
const $value = Points;
