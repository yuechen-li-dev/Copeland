record Point {
    x: number;
    y: number;
}

function update(): Point {
    let point: Point = { x: 1, y: 2 };
    point = point with {
        y: point.x,
        x: point.y,
    };
    const fixed: Point = point with { x: 10 };
    return fixed;
}
