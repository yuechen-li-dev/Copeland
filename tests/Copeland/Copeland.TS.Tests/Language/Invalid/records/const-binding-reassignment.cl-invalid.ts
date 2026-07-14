record Point { x: number; }
function bad(): Point {
    const point: Point = { x: 0 };
    point = point with { x: 1 };
    return point;
}
