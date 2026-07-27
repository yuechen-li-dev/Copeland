record AffineLength {
    px: int;
    uiNumerator: int;
}

record Frame {
    x: int;
    y: int;
    width: int;
    height: int;
}

function ResolveX(length: AffineLength, parentWidth: int): int {
    return length.px + Int.Truncate(Float.From(length.uiNumerator * parentWidth) / 1000.0);
}

function ResolveY(length: AffineLength, parentHeight: int): int {
    return length.px + Int.Truncate(Float.From(length.uiNumerator * parentHeight) / 1000.0);
}

function Run(iterations: int): int {
    const left: AffineLength = { px: 24, uiNumerator: 0 };
    const top: AffineLength = { px: 20, uiNumerator: 0 };
    const inset: AffineLength = { px: -12, uiNumerator: 250 };
    const fill: AffineLength = { px: 0, uiNumerator: 1000 };
    let checksum: int = 0;

    for (let round: int = 0; round < iterations; round = round + 1) {
        const root: Frame = { x: 0, y: 0, width: 1280 + (round % 31), height: 720 + (round % 17) };
        const anchored: Frame = {
            x: ResolveX(left, root.width),
            y: ResolveY(top, root.height),
            width: ResolveX(inset, root.width),
            height: ResolveY(inset, root.height)
        };
        let cursor: int = anchored.y;

        for (let node: int = 0; node < 96; node = node + 1) {
            const fixed: int = 18 + ((node % 5) * 7);
            const width: int = ResolveX(fill, anchored.width) - (node % 3);
            const item: Frame = {
                x: anchored.x + (node % 4),
                y: cursor,
                width: width,
                height: fixed
            };
            cursor = cursor + fixed + 6;
            checksum = checksum + item.x + item.y + item.width + item.height;
        }

        for (let node: int = 0; node < 24; node = node + 1) {
            const horizontal: Frame = {
                x: anchored.x + ((node % 3) * 40),
                y: anchored.y + (Int.Truncate(Float.From(node) / 3.0) * 28),
                width: 36,
                height: 20
            };
            checksum = checksum + horizontal.x + horizontal.y + horizontal.width + horizontal.height;
        }
    }

    return checksum;
}
