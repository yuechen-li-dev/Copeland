record Point {
    x: number;
    y: number;
}

record Envelope {
    point: Point;
    result: Point ! string;
}

enum Event {
    Moved(point: Point),
}

function accept(point: Point): Point {
    return point;
}

function compose(): Envelope {
    const argument: Point = accept({ x: 1, y: 2 });
    const event: Event = Event.Moved({ x: 3, y: 4 });
    const result: Point ! string = ok({ x: 5, y: 6 });
    return {
        point: argument,
        result: result,
    };
}

function recordError(): number ! Point {
    return err({ x: 7, y: 8 });
}

function replaceComposed(envelope: Envelope): Envelope {
    return envelope with {
        point: { x: 9, y: 10 },
        result: ok({ x: 11, y: 12 }),
    };
}
