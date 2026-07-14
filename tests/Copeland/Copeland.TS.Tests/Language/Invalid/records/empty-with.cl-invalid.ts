record Point { x: number; }
function bad(point: Point): Point { return point with {}; }
