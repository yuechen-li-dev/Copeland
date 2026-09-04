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
        export enum ProfileEdge {
            Top,
            Right,
            Bottom,
            Left,
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
        """;
}
