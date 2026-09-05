namespace Copeland.TS.Profiles;

/// <summary>
/// The ordinary Copeland declarations used by Profile template libraries.
/// They are source-level types and functions, not parser forms or a geometry
/// evaluator. Their payload-enum values are erased into Profile IR by the
/// Profile host before runtime lowering.
/// </summary>
public static class ProfileTemplateFunctions
{
    public const string Source = """
        export record ProfileStyle {
            fill: string;
        }

        export record ProfileLayerId {
            name: string;
        }

        export enum ProfileEdge {
            Top,
            Right,
            Bottom,
            Left,
        }

        export record ConceptPoint {
            x: number;
            y: number;
        }

        export record ConceptPath {
            start: ConceptPoint;
            end: ConceptPoint;
        }

        export function SpanOf<T>(elements: T[]): Span<T> {
            return elements;
        }

        export function Point(x: number, y: number): ConceptPoint {
            return { x: x, y: y };
        }

        export function PathBetween(start: ConceptPoint, end: ConceptPoint): ConceptPath {
            return { start: start, end: end };
        }

        export function Midpoint(path: ConceptPath): ConceptPoint {
            return Along(path, 0.5);
        }

        export function Along(path: ConceptPath, t: number): ConceptPoint {
            return {
                x: path.start.x + ((path.end.x - path.start.x) * t),
                y: path.start.y + ((path.end.y - path.start.y) * t)
            };
        }

        export function OffsetPoint(point: ConceptPoint, x: number, y: number): ConceptPoint {
            return { x: point.x + x, y: point.y + y };
        }

        export record CircleArgs {
            radius: number;
            x?: number;
            y?: number;
        }

        export record RectangleArgs {
            width: number;
            height: number;
        }

        export record RoundedRectangleArgs {
            width: number;
            height: number;
            radius: number;
        }

        export record EllipseArgs {
            radiusX: number;
            radiusY: number;
            x?: number;
            y?: number;
        }

        export record SlotArgs {
            length: number;
            width: number;
            angle?: number;
            x?: number;
            y?: number;
        }

        export record CapsuleArgs {
            from: ConceptPoint;
            to: ConceptPoint;
            width: number;
        }

        export record RegularPolygonArgs {
            sides: int;
            radius: number;
            rotation?: number;
        }

        export record PolygonArgs {
            points: number[][];
        }

        export enum ProfileShape {
            Circle(args: CircleArgs),
            Rectangle(args: RectangleArgs),
            RoundedRectangle(args: RoundedRectangleArgs),
            Ellipse(args: EllipseArgs),
            Slot(args: SlotArgs),
            Capsule(args: CapsuleArgs),
            RegularPolygon(args: RegularPolygonArgs),
            Polygon(args: PolygonArgs),
        }

        export function Circle(args: CircleArgs): ProfileShape {
            return ProfileShape.Circle(args);
        }

        export function Rectangle(args: RectangleArgs): ProfileShape {
            return ProfileShape.Rectangle(args);
        }

        export function RoundedRectangle(args: RoundedRectangleArgs): ProfileShape {
            return ProfileShape.RoundedRectangle(args);
        }

        export function Ellipse(args: EllipseArgs): ProfileShape {
            return ProfileShape.Ellipse(args);
        }

        export function Slot(args: SlotArgs): ProfileShape {
            return ProfileShape.Slot(args);
        }

        export function Capsule(args: CapsuleArgs): ProfileShape {
            return ProfileShape.Capsule(args);
        }

        export function Tube(args: CapsuleArgs): ProfileShape {
            return Capsule(args);
        }

        export function RegularPolygon(args: RegularPolygonArgs): ProfileShape {
            return ProfileShape.RegularPolygon(args);
        }

        export function Polygon(args: PolygonArgs): ProfileShape {
            return ProfileShape.Polygon(args);
        }

        export record ShapeOperationArgs {
            id: string;
            as: string;
            shape: ProfileShape;
        }

        export record HoleArgs {
            id: string;
            as: string;
            radius: number;
            x?: number;
            y?: number;
        }

        export record EdgeOperationArgs {
            id: string;
            as: string;
            edge: ProfileEdge;
            width: number;
            depth: number;
            position?: number;
        }

        export record RepeatRadialArgs {
            id: string;
            as: string;
            count: int;
            toothDepth: number;
            toothFraction?: number;
            rotation?: number;
        }

        export record TranslateArgs {
            id: string;
            as: string;
            x: number;
            y: number;
        }

        export record RotateArgs {
            id: string;
            as: string;
            degrees: number;
        }

        export record ScaleArgs {
            id: string;
            as: string;
            x: number;
            y: number;
        }

        export record MirrorArgs {
            id: string;
            as: string;
            axis: string;
        }

        export record ArcArgs {
            bulge: number;
        }

        export record BulgeArgs {
            amount: number;
        }

        export record SplineArgs {
            control1: ConceptPoint;
            control2: ConceptPoint;
        }

        export enum SegmentCurve {
            Arc(args: ArcArgs),
            Bulge(args: BulgeArgs),
            Spline(args: SplineArgs),
        }

        export function Arc(args: ArcArgs): SegmentCurve {
            return SegmentCurve.Arc(args);
        }

        export function Bulge(args: BulgeArgs): SegmentCurve {
            return SegmentCurve.Bulge(args);
        }

        export function Spline(args: SplineArgs): SegmentCurve {
            return SegmentCurve.Spline(args);
        }

        export record SelectedProfileSegmentArgs {
            owner: string;
            index: int;
        }

        export record LineProfileSegmentArgs {
            start: ConceptPoint;
            end: ConceptPoint;
        }

        export record CurvedProfileSegmentArgs {
            start: ConceptPoint;
            end: ConceptPoint;
            curve: SegmentCurve;
        }

        export enum ProfileSegment {
            Selected(args: SelectedProfileSegmentArgs),
            Line(args: LineProfileSegmentArgs),
            Curve(args: CurvedProfileSegmentArgs),
        }

        export function SelectSegment(owner: string, index: int): ProfileSegment {
            return ProfileSegment.Selected({ owner: owner, index: index });
        }

        export function LineSegment(start: ConceptPoint, end: ConceptPoint): ProfileSegment {
            return ProfileSegment.Line({ start: start, end: end });
        }

        export function CurveSegment(start: ConceptPoint, end: ConceptPoint, curve: SegmentCurve): ProfileSegment {
            return ProfileSegment.Curve({ start: start, end: end, curve: curve });
        }

        export record DovetailTabArgs {
            start: ConceptPoint;
            end: ConceptPoint;
            leftShoulder: ConceptPoint;
            rightShoulder: ConceptPoint;
        }

        export function DovetailTab(args: DovetailTabArgs): Span<ProfileSegment> {
            return SpanOf([
                LineSegment(args.start, args.leftShoulder),
                LineSegment(args.leftShoulder, args.rightShoulder),
                LineSegment(args.rightShoulder, args.end)
            ]);
        }

        export record VNotchArgs {
            start: ConceptPoint;
            tip: ConceptPoint;
            end: ConceptPoint;
        }

        export function VNotch(args: VNotchArgs): Span<ProfileSegment> {
            return SpanOf([
                LineSegment(args.start, args.tip),
                LineSegment(args.tip, args.end)
            ]);
        }

        export record GearToothArgs {
            rootLeft: ConceptPoint;
            tipLeft: ConceptPoint;
            tipRight: ConceptPoint;
            rootRight: ConceptPoint;
        }

        export function GearTooth(args: GearToothArgs): Span<ProfileSegment> {
            return SpanOf([
                LineSegment(args.rootLeft, args.tipLeft),
                LineSegment(args.tipLeft, args.tipRight),
                LineSegment(args.tipRight, args.rootRight)
            ]);
        }

        export record ReplaceSegmentArgs {
            id: string;
            as: string;
            segment: int;
            replacement: SegmentCurve;
        }

        export record ReplaceSpanArgs {
            id: string;
            as: string;
            target: Span<ProfileSegment>;
            replacement: Span<ProfileSegment>;
        }

        export enum ProfileOperation {
            Add(args: ShapeOperationArgs),
            Subtract(args: ShapeOperationArgs),
            Hole(args: HoleArgs),
            Tab(args: EdgeOperationArgs),
            Notch(args: EdgeOperationArgs),
            RepeatRadial(args: RepeatRadialArgs),
            Translate(args: TranslateArgs),
            Rotate(args: RotateArgs),
            Scale(args: ScaleArgs),
            Mirror(args: MirrorArgs),
            ReplaceSegment(args: ReplaceSegmentArgs),
            ReplaceSpan(args: ReplaceSpanArgs),
        }

        export function Add(args: ShapeOperationArgs): ProfileOperation {
            return ProfileOperation.Add(args);
        }

        export function Subtract(args: ShapeOperationArgs): ProfileOperation {
            return ProfileOperation.Subtract(args);
        }

        export function Hole(args: HoleArgs): ProfileOperation {
            return ProfileOperation.Hole(args);
        }

        export function Tab(args: EdgeOperationArgs): ProfileOperation {
            return ProfileOperation.Tab(args);
        }

        export function Notch(args: EdgeOperationArgs): ProfileOperation {
            return ProfileOperation.Notch(args);
        }

        export function RepeatRadial(args: RepeatRadialArgs): ProfileOperation {
            return ProfileOperation.RepeatRadial(args);
        }

        export function Translate(args: TranslateArgs): ProfileOperation {
            return ProfileOperation.Translate(args);
        }

        export function Rotate(args: RotateArgs): ProfileOperation {
            return ProfileOperation.Rotate(args);
        }

        export function Scale(args: ScaleArgs): ProfileOperation {
            return ProfileOperation.Scale(args);
        }

        export function Mirror(args: MirrorArgs): ProfileOperation {
            return ProfileOperation.Mirror(args);
        }

        export function ReplaceSegment(args: ReplaceSegmentArgs): ProfileOperation {
            return ProfileOperation.ReplaceSegment(args);
        }

        export function ReplaceSpan(args: ReplaceSpanArgs): ProfileOperation {
            return ProfileOperation.ReplaceSpan(args);
        }

        export record ProfileSource {
            name: string;
            shape: ProfileShape;
            operations: ProfileOperation[];
            yieldState: string;
            baseState?: string;
            style?: ProfileStyle;
        }

        export record ProfileLayer {
            id: ProfileLayerId;
            profiles: ProfileSource[];
        }

        export record ProfileComposition {
            layers: ProfileLayer[];
        }

        export function LayerId(name: string): ProfileLayerId {
            return { name: name };
        }

        export function Profile(args: ProfileSource): ProfileSource {
            return args;
        }

        export function Layer(name: string, profiles: ProfileSource[]): ProfileLayer {
            return { id: LayerId(name), profiles: profiles };
        }

        export function Layers(layers: ProfileLayer[]): ProfileComposition {
            return { layers: layers };
        }
        """;
}
