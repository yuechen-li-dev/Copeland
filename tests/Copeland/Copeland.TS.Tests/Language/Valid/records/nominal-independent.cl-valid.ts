record ScreenPoint {
    x: number;
    y: number;
}

record WorldPoint {
    x: number;
    y: number;
}

function screen(): ScreenPoint {
    return { x: 0, y: 0 };
}

function world(): WorldPoint {
    return { x: 0, y: 0 };
}
