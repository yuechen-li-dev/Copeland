record Point {
    x: number;
    y: number;
}

function first(): number { return 1; }
function second(): number { return 2; }

function origin(): Point {
    const point: Point = {
        y: second(),
        x: first(),
    };
    const x: number = point.x;
    return point;
}
