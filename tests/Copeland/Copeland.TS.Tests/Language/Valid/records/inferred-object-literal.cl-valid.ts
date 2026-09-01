function value(): int {
    const point = { x: 40, y: 2 };
    const moved = point with { x: point.x };
    return moved.x + moved.y;
}
