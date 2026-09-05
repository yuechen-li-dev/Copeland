// A three-color cut-paper sketch. The evidence runner compiles each Layer
// independently, then ProfileSvgExporter exports canonical layers in paint order.
// Coordinates are logical units, +Y upward. No SVG geometry lives here.
record Vec2 {
    x: number;
    y: number;
}

record BicycleLayout {
    rear: Vec2;
    front: Vec2;
    wheelRadius: number;
    seat: Vec2;
    handlebar: Vec2;
    pedal: Vec2;
}

record PelicanLayout {
    body: Vec2;
    head: Vec2;
    wing: Vec2;
    knee: Vec2;
    foot: Vec2;
    beakLength: number;
    headTilt: number;
    wingScale: number;
}

const WheelRadius: number = 59.8;
const Wheelbase: number = 160.0;
const BeakLength: number = 98.0;
const BodyLift: number = 44.0;
const HeadTilt: number = 0.0;
const WingScale: number = 1.0;
const Layer: int = 0;

const InkStyle: ProfileStyle = { fill: "#193747" };
const BicycleStyle: ProfileStyle = InkStyle with { fill: "#238f91" };
const AccentStyle: ProfileStyle = InkStyle with { fill: "#e6a52e" };

function BuildLayerStyle(layer: int, ink: ProfileStyle, bicycle: ProfileStyle, accent: ProfileStyle): ProfileStyle {
    if (layer >= 2 && layer <= 8) {
        return bicycle;
    }
    if (layer >= 9 && layer <= 11) {
        return accent;
    }
    if (layer == 15) {
        return accent;
    }
    return ink;
}

function Offset(point: Vec2, x: number, y: number): Vec2 {
    return { x: point.x + x, y: point.y + y };
}

function BuildBicycleLayout(wheelRadius: number, wheelbase: number): BicycleLayout {
    const rear: Vec2 = { x: 100.0, y: wheelRadius + 12.0 };
    const front: Vec2 = Offset(rear, wheelbase, 0.0);
    const seat: Vec2 = Offset(rear, wheelbase * 0.33, 78.0);
    const pedal: Vec2 = Offset(rear, wheelbase * 0.60, 4.0);
    const handlebar: Vec2 = Offset(front, -16.0, 104.0);
    return { rear, front, wheelRadius: wheelRadius, seat, handlebar, pedal };
}

function BuildPelicanLayout(bike: BicycleLayout, bodyLift: number, beakLength: number, headTilt: number, wingScale: number): PelicanLayout {
    const body: Vec2 = Offset(bike.seat, 0.0, bodyLift);
    const head: Vec2 = Offset(body, 65.0, 82.0);
    const wing: Vec2 = Offset(body, -8.0, 4.0);
    const knee: Vec2 = Offset(body, 38.0, -44.0);
    const foot: Vec2 = Offset(bike.pedal, 10.0, -4.0);
    return { body, head, wing, knee, foot, beakLength: beakLength,
        headTilt: headTilt, wingScale: wingScale };
}

const Bicycle: BicycleLayout = BuildBicycleLayout(WheelRadius, Wheelbase);
const Pelican: PelicanLayout = BuildPelicanLayout(Bicycle, BodyLift, BeakLength, HeadTilt, WingScale);

function Absolute(value: number): number {
    if (value < 0.0) {
        return -value;
    }
    return value;
}

// Local polygon escape: a filled segment. Manhattan normalization is adequate
// for this sketch; a real Capsule/PolylineStroke would remove this arithmetic.
function Tube(start: Vec2, end: Vec2, width: number): ProfileShape {
    const dx: number = end.x - start.x;
    const dy: number = end.y - start.y;
    const length: number = Absolute(dx) + Absolute(dy);
    const nx: number = -dy * width / length;
    const ny: number = dx * width / length;
    return Polygon({ points: [
        [start.x + nx, start.y + ny],
        [end.x + nx, end.y + ny],
        [end.x - nx, end.y - ny],
        [start.x - nx, start.y - ny]
    ] });
}

function BuildRearWheel(bike: BicycleLayout): ProfileShape {
    return Circle({ radius: bike.wheelRadius, x: bike.rear.x, y: bike.rear.y });
}

function BuildFrontWheel(bike: BicycleLayout): ProfileShape {
    return Circle({ radius: bike.wheelRadius, x: bike.front.x, y: bike.front.y });
}

function BuildFrame(bike: BicycleLayout): ProfileShape {
    return Polygon({ points: [
        [bike.rear.x, bike.rear.y],
        [bike.seat.x, bike.seat.y],
        [bike.pedal.x, bike.pedal.y]
    ] });
}

function BuildFrontFrame(bike: BicycleLayout): ProfileShape {
    return Polygon({ points: [
        [bike.seat.x, bike.seat.y],
        [bike.handlebar.x, bike.handlebar.y - 22.0],
        [bike.pedal.x, bike.pedal.y]
    ] });
}

function BuildPelicanBody(bird: PelicanLayout): ProfileShape {
    return Ellipse({ radiusX: 65.0, radiusY: 39.0, x: bird.body.x, y: bird.body.y });
}

function BuildPelicanHead(bird: PelicanLayout): ProfileShape {
    return Circle({ radius: 21.0 });
}

// Local polygon escape: broad hanging throat pouch and a long tapered bill.
// All vertices are head-local and depend on one beak-length parameter.
function BuildPelicanBeak(bird: PelicanLayout): ProfileShape {
    const length: number = bird.beakLength;
    return Polygon({ points: [
        [12.0, 5.0],
        [length + 9.0, -3.0],
        [length + 12.0, -8.0],
        [length * 0.70, -27.0],
        [length * 0.28, -31.0],
        [13.0, -14.0]
    ] });
}

function BuildPelicanWing(bird: PelicanLayout): ProfileOperation {
    return Subtract({ id: "FoldedWing", as: "WingCut", shape: Ellipse({
        radiusX: 40.0 * bird.wingScale,
        radiusY: 20.0 * bird.wingScale,
        x: bird.wing.x, y: bird.wing.y
    }) });
}

function BuildPelicanTail(bird: PelicanLayout): ProfileShape {
    return Polygon({ points: [
        [bird.body.x - 58.0, bird.body.y + 13.0],
        [bird.body.x - 84.0, bird.body.y + 28.0],
        [bird.body.x - 64.0, bird.body.y - 11.0],
        [bird.body.x - 58.0, bird.body.y - 17.0]
    ] });
}

function BuildPelicanLegs(bird: PelicanLayout): ProfileShape {
    return Tube(Offset(bird.body, 2.0, -22.0), bird.knee, 5.0);
}

// Explicit paint order; each case is a separately compiled static Profile.
function BuildLayer(layer: int, bike: BicycleLayout, bird: PelicanLayout): ProfileShape {
    if (layer == 0) {
        return BuildRearWheel(bike);
    }
    if (layer == 1) {
        return BuildFrontWheel(bike);
    }
    if (layer == 2) {
        return BuildFrame(bike);
    }
    if (layer == 3) {
        return BuildFrontFrame(bike);
    }
    if (layer == 4) {
        return Tube(bike.front, bike.handlebar, 4.0);
    }
    if (layer == 5) {
        return Tube(Offset(bike.seat, -17.0, 0.0), Offset(bike.seat, 18.0, 0.0), 4.0);
    }
    if (layer == 6) {
        return Tube(Offset(bike.handlebar, -13.0, 0.0), Offset(bike.handlebar, 19.0, 0.0), 4.0);
    }
    if (layer == 7) {
        return Circle({ radius: 10.0, x: bike.pedal.x, y: bike.pedal.y });
    }
    if (layer == 8) {
        return Tube(bike.pedal, bird.foot, 3.0);
    }
    if (layer == 9) {
        return BuildPelicanLegs(bird);
    }
    if (layer == 10) {
        return Tube(bird.knee, bird.foot, 4.0);
    }
    if (layer == 11) {
        return Tube(Offset(bird.foot, -8.0, -2.0), Offset(bird.foot, 17.0, -2.0), 4.0);
    }
    if (layer == 12) {
        return BuildPelicanTail(bird);
    }
    if (layer == 13) {
        return Ellipse({ radiusX: 13.0, radiusY: 42.0 });
    }
    if (layer == 14) {
        return BuildPelicanBody(bird);
    }
    if (layer == 15) {
        return BuildPelicanBeak(bird);
    }
    return BuildPelicanHead(bird);
}

function FinishLayer(layer: int, bike: BicycleLayout, bird: PelicanLayout): ProfileOperation[] {
    if (layer == 0) {
        return [Hole({ id: "RearRim", as: "Finished", radius: bike.wheelRadius - 5.0, x: bike.rear.x, y: bike.rear.y })];
    }
    if (layer == 1) {
        return [Hole({ id: "FrontRim", as: "Finished", radius: bike.wheelRadius - 5.0, x: bike.front.x, y: bike.front.y })];
    }
    if (layer == 2) {
        return [Subtract({ id: "RearTriangle", as: "Finished", shape: Polygon({ points: [
            [bike.rear.x + 13.0, bike.rear.y + 7.0],
            [bike.seat.x - 1.0, bike.seat.y - 15.0],
            [bike.pedal.x - 12.0, bike.pedal.y + 7.0]
        ] }) })];
    }
    if (layer == 3) {
        return [Subtract({ id: "FrontTriangle", as: "Finished", shape: Polygon({ points: [
            [bike.seat.x + 12.0, bike.seat.y - 6.0],
            [bike.handlebar.x - 12.0, bike.handlebar.y - 29.0],
            [bike.pedal.x + 1.0, bike.pedal.y + 15.0]
        ] }) })];
    }
    if (layer == 13) {
        return [
            Rotate({ id: "NeckLean", as: "Leaning", degrees: -24.0 }),
            Translate({ id: "NeckAnchor", as: "Placed", x: bird.body.x + 49.0, y: bird.body.y + 51.0 }),
            Translate({ id: "Finish", as: "Finished", x: 0.0, y: 0.0 })
        ];
    }
    if (layer == 14) {
        return [BuildPelicanWing(bird), Translate({ id: "Finish", as: "Finished", x: 0.0, y: 0.0 })];
    }
    if (layer == 15) {
        return [
            Subtract({ id: "BillSeam", as: "Seamed", shape: Polygon({ points: [
                [25.0, -5.0],
                [bird.beakLength - 3.0, -8.0],
                [25.0, -9.0]
            ] }) }),
            Rotate({ id: "BeakTilt", as: "Tilted", degrees: bird.headTilt }),
            Translate({ id: "BeakAnchor", as: "Placed", x: bird.head.x, y: bird.head.y }),
            Translate({ id: "Finish", as: "Finished", x: 0.0, y: 0.0 })
        ];
    }
    if (layer == 16) {
        return [
            Hole({ id: "Eye", as: "EyeCut", radius: 3.5, x: 5.0, y: 10.0 }),
            Rotate({ id: "HeadTilt", as: "Tilted", degrees: bird.headTilt }),
            Translate({ id: "HeadAnchor", as: "Placed", x: bird.head.x, y: bird.head.y }),
            Translate({ id: "Finish", as: "Finished", x: 0.0, y: 0.0 })
        ];
    }
    return [Translate({ id: "Finish", as: "Finished", x: 0.0, y: 0.0 })];
}

export default (
    <Profile name="PelicanBicycleLayer"
        base={BuildLayer(Layer, Bicycle, Pelican)}
        style={BuildLayerStyle(Layer, InkStyle, BicycleStyle, AccentStyle)}>
        {FinishLayer(Layer, Bicycle, Pelican)}
        {Yield(Finished)}
    </Profile>
);
