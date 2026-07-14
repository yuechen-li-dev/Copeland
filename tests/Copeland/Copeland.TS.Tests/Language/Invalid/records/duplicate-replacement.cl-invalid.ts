record Point { x: number; }
function bad(point: Point): Point { return point with { x: 1, x: 2 }; }
