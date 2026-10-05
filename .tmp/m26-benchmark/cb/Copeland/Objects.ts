record Point {
    x: int;
    y: float;
    tag: string;
}

function indices(count: int): int[] {
    const buffer: MutableArray<int> = MutableArray<int>(count);
    for (let i: int = 0; i < count; i = i + 1) {
        buffer[i] = i;
    }
    return buffer.freeze();
}

function tagFor(id: int): string {
    if (id % 2 == 0) {
        return "a";
    }
    return "b";
}

function makePoints(ids: int[]): Point[] {
    return batch ids as id {
        const point: Point = { x: id, y: Float.From(id) * 0.5, tag: tagFor(id) };
        return point;
    };
}

export function runObjects(rounds: int, count: int): float {
    const ids: int[] = indices(count);
    let sum: float = 0.0;
    for (let round: int = 0; round < rounds; round = round + 1) {
        const points: Point[] = makePoints(ids);
        for (const point of points) {
            if (point.tag == "a") {
                sum = sum + Float.From(point.x) + point.y;
            }
        }
    }
    return sum;
}
