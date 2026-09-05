// A three-color cut-paper sketch. Named layer source order is painter order;
// each Profile still resolves independently through the ordinary geometry path.
// Coordinates are logical units, +Y upward. No SVG geometry lives here.
record BicycleLayout {
    rear: ConceptPoint;
    front: ConceptPoint;
    wheelRadius: number;
    seat: ConceptPoint;
    handlebar: ConceptPoint;
    pedal: ConceptPoint;
    forkGuide: ConceptPath;
    topTubeGuide: ConceptPath;
    seatGuide: ConceptPath;
    handlebarGuide: ConceptPath;
}

record PelicanLayout {
    body: ConceptPoint;
    head: ConceptPoint;
    wing: ConceptPoint;
    knee: ConceptPoint;
    foot: ConceptPoint;
    upperLegGuide: ConceptPath;
    lowerLegGuide: ConceptPath;
    beakAxis: ConceptPath;
    beakLength: number;
    headTilt: number;
    wingScale: number;
}

const WheelRadius: number = 52.0;
const Wheelbase: number = 160.0;
const BeakLength: number = 98.0;
const BodyLift: number = 44.0;
const HeadTilt: number = 0.0;
const WingScale: number = 1.0;
const InkStyle: ProfileStyle = { fill: "#193747" };
const BicycleStyle: ProfileStyle = InkStyle with { fill: "#238f91" };
const AccentStyle: ProfileStyle = InkStyle with { fill: "#e6a52e" };

function Offset(point: ConceptPoint, x: number, y: number): ConceptPoint {
    return OffsetPoint(point, x, y);
}

function BuildBicycleLayout(wheelRadius: number, wheelbase: number): BicycleLayout {
    const rear: ConceptPoint = Point(100.0, wheelRadius + 12.0);
    const front: ConceptPoint = Offset(rear, wheelbase, 0.0);
    const seat: ConceptPoint = Offset(rear, wheelbase * 0.33, 78.0);
    const pedal: ConceptPoint = Offset(rear, wheelbase * 0.60, 4.0);
    const handlebar: ConceptPoint = Offset(front, -16.0, 104.0);
    return { rear, front, wheelRadius: wheelRadius, seat, handlebar, pedal,
        forkGuide: PathBetween(front, handlebar),
        topTubeGuide: PathBetween(seat, Offset(handlebar, 0.0, -22.0)),
        seatGuide: PathBetween(Offset(seat, -17.0, 0.0), Offset(seat, 18.0, 0.0)),
        handlebarGuide: PathBetween(Offset(handlebar, -13.0, 0.0), Offset(handlebar, 19.0, 0.0)) };
}

function BuildPelicanLayout(bike: BicycleLayout, bodyLift: number, beakLength: number, headTilt: number, wingScale: number): PelicanLayout {
    const body: ConceptPoint = Offset(bike.seat, 0.0, bodyLift);
    const head: ConceptPoint = Offset(body, 65.0, 82.0);
    const wing: ConceptPoint = Offset(body, -8.0, 4.0);
    const knee: ConceptPoint = Offset(body, 38.0, -44.0);
    const foot: ConceptPoint = Offset(bike.pedal, 10.0, -4.0);
    return { body, head, wing, knee, foot,
        upperLegGuide: PathBetween(Offset(body, 2.0, -22.0), knee),
        lowerLegGuide: PathBetween(knee, foot),
        beakAxis: PathBetween(head, Offset(head, beakLength, -4.0)),
        beakLength: beakLength,
        headTilt: headTilt, wingScale: wingScale };
}

const Bicycle: BicycleLayout = BuildBicycleLayout(WheelRadius, Wheelbase);
const Pelican: PelicanLayout = BuildPelicanLayout(Bicycle, BodyLift, BeakLength, HeadTilt, WingScale);

function TubeFromGuide(guide: ConceptPath, width: number): ProfileShape {
    return Tube({ from: guide.start, to: guide.end, width: width });
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
    return TubeFromGuide(bird.upperLegGuide, 10.0);
}

function PaintProfile(name: string, shape: ProfileShape, operations: ProfileOperation[], yieldState: string, style: ProfileStyle): ProfileSource {
    return Profile({
        name: name,
        shape: shape,
        operations: operations,
        yieldState: yieldState,
        style: style
    });
}

// Semantic layer source order is painter order. Item order is painter order
// within a layer. This is an ordinary typed helper, not a retained scene tree.
function BicycleLayers(bike: BicycleLayout, bird: PelicanLayout, ink: ProfileStyle, bicycle: ProfileStyle, accent: ProfileStyle): ProfileLayer[] {
    const WheelsLayer: ProfileLayer = Layer("Wheels", [
            PaintProfile("RearWheel", BuildRearWheel(bike), [
                Hole({ id: "RearRim", as: "Finished", radius: bike.wheelRadius - 5.0, x: bike.rear.x, y: bike.rear.y })
            ], "Finished", ink),
            PaintProfile("FrontWheel", BuildFrontWheel(bike), [
                Hole({ id: "FrontRim", as: "Finished", radius: bike.wheelRadius - 5.0, x: bike.front.x, y: bike.front.y })
            ], "Finished", ink)
    ]);
    const BicycleFrameLayer: ProfileLayer = Layer("Bicycle Frame", [
            PaintProfile("RearFrame", BuildFrame(bike), [
                Subtract({ id: "RearTriangle", as: "Finished", shape: Polygon({ points: [
                    [bike.rear.x + 13.0, bike.rear.y + 7.0],
                    [bike.seat.x - 1.0, bike.seat.y - 15.0],
                    [bike.pedal.x - 12.0, bike.pedal.y + 7.0]
                ] }) })
            ], "Finished", bicycle),
            PaintProfile("FrontFrame", TubeFromGuide(bike.topTubeGuide, 8.0), [], "Base", bicycle),
            PaintProfile("Fork", TubeFromGuide(bike.forkGuide, 8.0), [], "Base", bicycle),
            PaintProfile("Seat", TubeFromGuide(bike.seatGuide, 8.0), [], "Base", bicycle),
            PaintProfile("Handlebar", TubeFromGuide(bike.handlebarGuide, 8.0), [], "Base", bicycle),
            PaintProfile("Crank", Circle({ radius: 10.0, x: bike.pedal.x, y: bike.pedal.y }), [], "Base", bicycle),
            PaintProfile("CrankArm", Tube({ from: bike.pedal, to: bird.foot, width: 6.0 }), [], "Base", bicycle)
    ]);
    const PelicanLegsLayer: ProfileLayer = Layer("Pelican Legs", [
            PaintProfile("UpperLeg", BuildPelicanLegs(bird), [], "Base", accent),
            PaintProfile("LowerLeg", TubeFromGuide(bird.lowerLegGuide, 8.0), [], "Base", accent),
            PaintProfile("WebbedFoot", Tube({ from: Offset(bird.foot, -8.0, -2.0), to: Offset(bird.foot, 17.0, -2.0), width: 8.0 }), [], "Base", accent)
    ]);
    const PelicanBodyLayer: ProfileLayer = Layer("Pelican Body", [
            PaintProfile("Tail", BuildPelicanTail(bird), [], "Base", ink),
            PaintProfile("Neck", Ellipse({ radiusX: 13.0, radiusY: 42.0 }), [
                Rotate({ id: "NeckLean", as: "Leaning", degrees: -24.0 }),
                Translate({ id: "NeckAnchor", as: "Placed", x: bird.body.x + 49.0, y: bird.body.y + 51.0 })
            ], "Placed", ink),
            PaintProfile("BodyAndWing", BuildPelicanBody(bird), [
                ReplaceSegment({ id: "BodyCurve", as: "CurvedBody", segment: 0,
                    replacement: Bulge({ amount: 8.0 }) }),
                BuildPelicanWing(bird)
            ], "WingCut", ink)
    ]);
    const PelicanDetailsLayer: ProfileLayer = Layer("Pelican Details", [
            PaintProfile("Beak", BuildPelicanBeak(bird), [
                ReplaceSegment({ id: "BeakTopCurve", as: "CurvedBill", segment: 0,
                    replacement: Arc({ bulge: 4.0 }) }),
                Subtract({ id: "BillSeam", as: "Seamed", shape: Polygon({ points: [
                    [25.0, -5.0],
                    [bird.beakLength - 3.0, -8.0],
                    [25.0, -9.0]
                ] }) }),
                Rotate({ id: "BeakTilt", as: "Tilted", degrees: bird.headTilt }),
                Translate({ id: "BeakAnchor", as: "Placed", x: bird.head.x, y: bird.head.y })
            ], "Placed", accent),
            PaintProfile("HeadAndEye", BuildPelicanHead(bird), [
                Hole({ id: "Eye", as: "EyeCut", radius: 3.5, x: 5.0, y: 10.0 }),
                Rotate({ id: "HeadTilt", as: "Tilted", degrees: bird.headTilt }),
                Translate({ id: "HeadAnchor", as: "Placed", x: bird.head.x, y: bird.head.y })
            ], "Placed", ink)
    ]);
    return [
        WheelsLayer,
        BicycleFrameLayer,
        PelicanLegsLayer,
        PelicanBodyLayer,
        PelicanDetailsLayer
    ];
}

export default (Layers(BicycleLayers(Bicycle, Pelican, InkStyle, BicycleStyle, AccentStyle)));
