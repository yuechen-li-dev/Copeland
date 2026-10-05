using System.Globalization;
using System.Text;
namespace Copeland.Profile;

internal sealed record RadialPatternTarget(int StartSegmentIndex, int SegmentCount);

internal sealed record RadialPatternTargetLayout(
    VectorShape Shape,
    IReadOnlyList<RadialPatternTarget> Targets);

internal sealed record RefinedSpanInterval(
    VectorShape Shape,
    int StartSegmentIndex,
    int SegmentCount,
    IReadOnlyList<int> SourceSegmentIndexes);

internal static class ProfileGeometry
{
    private const double CircleControl = 0.5522847498307936;

    public static VectorShape Create(ProfileShapeSpec shape)
    {
        return shape switch
        {
            RectangleProfileShape rectangle => Rectangle(rectangle.Width, rectangle.Height),
            RoundedRectangleProfileShape rounded => RoundedRectangle(rounded.Width, rounded.Height, rounded.Radius),
            CircleProfileShape circle => Ellipse(circle.Radius, circle.Radius, circle.CenterX, circle.CenterY),
            EllipseProfileShape ellipse => Ellipse(ellipse.RadiusX, ellipse.RadiusY, ellipse.CenterX, ellipse.CenterY),
            SlotProfileShape slot => Slot(slot.Length, slot.Width, slot.AngleDegrees, slot.CenterX, slot.CenterY),
            CapsuleProfileShape capsule => Capsule(capsule.From, capsule.To, capsule.Width),
            RegularPolygonProfileShape polygon => RegularPolygon(polygon.Sides, polygon.Radius, polygon.RotationDegrees),
            PolygonProfileShape polygon => Polygon(polygon.Points),
            _ => throw new InvalidOperationException($"Unknown profile shape '{shape.Kind}'."),
        };
    }

    public static VectorShape Add(VectorShape input, ProfileShapeSpec addition)
    {
        VectorShape added = Create(addition);
        if (Intersects(input.Bounds, added.Bounds))
        {
            throw new ProfileResolutionException(
                "COPE-PROFILE-0035",
                "M0 generic Add accepts disjoint regions only; use a semantic feature such as Tab for attached geometry.",
                addition.Span);
        }
        VectorContour[] outerContours = added.Contours.Select(contour => WithRole(contour, VectorContourRole.Outer)).ToArray();
        return new VectorShape(input.Contours.Concat(outerContours).ToArray());
    }

    public static VectorShape SubtractContained(VectorShape input, ProfileShapeSpec subtraction)
    {
        VectorShape removed = Create(subtraction);
        if (!Contains(input.Bounds, removed.Bounds))
        {
            throw new ProfileResolutionException("COPE-PROFILE-0031", "M0 subtraction must be fully contained by the current profile.", subtraction.Span);
        }
        VectorContour[] holes = removed.Contours.Select(contour => WithRole(contour, VectorContourRole.Hole)).ToArray();
        return new VectorShape(input.Contours.Concat(holes).ToArray());
    }

    public static VectorShape Gear(CircleProfileShape circle, int count, double toothDepth, double toothFraction, double rotationDegrees)
    {
        int steps = count * 4;
        var points = new VectorPoint[steps];
        double rotation = Degrees(rotationDegrees);
        for (int index = 0; index < steps; index++)
        {
            int phase = index % 4;
            double halfWidth = Math.Clamp(toothFraction, 0.05, 0.95) / 2d;
            double phaseOffset = phase switch
            {
                0 => -halfWidth,
                1 => halfWidth,
                2 => 0.5 - halfWidth,
                _ => 0.5 + halfWidth,
            };
            double angle = rotation + (2d * Math.PI * ((index / 4) + phaseOffset) / count);
            double radius = phase is 0 or 1 ? circle.Radius + toothDepth : circle.Radius;
            points[index] = new VectorPoint(
                circle.CenterX + (Math.Cos(angle) * radius),
                circle.CenterY + (Math.Sin(angle) * radius));
        }
        return Polygon(points);
    }

    public static RadialPatternTargetLayout RadialPatternTargets(
        CircleProfileShape circle,
        int count,
        double targetFraction,
        double rotationDegrees)
    {
        double pitch = 4d / count;
        double targetStartAngle = Degrees(rotationDegrees) + (Math.PI * targetFraction / count);
        double targetStartParameter = ((Math.PI / 2d) - targetStartAngle) / (Math.PI / 2d);
        targetStartParameter %= 4d;
        if (targetStartParameter < 0)
        {
            targetStartParameter += 4d;
        }
        var boundaries = new SortedSet<double> { targetStartParameter, targetStartParameter + 4d };
        for (int index = 0; index < count; index++)
        {
            boundaries.Add(targetStartParameter + (index * pitch));
            boundaries.Add(targetStartParameter + ((index + targetFraction) * pitch));
            boundaries.Add(targetStartParameter + ((index + 1) * pitch));
        }
        for (int quadrantBoundary = (int)Math.Ceiling(targetStartParameter);
            quadrantBoundary < targetStartParameter + 4d;
            quadrantBoundary++)
        {
            boundaries.Add(quadrantBoundary);
        }

        VectorCubic[] quadrants = CircleCubics(circle, Math.PI / 2d);
        double[] ordered = boundaries.ToArray();
        var segments = new List<VectorSegment>();
        var targets = new List<RadialPatternTarget>();
        int? targetStart = null;
        for (int index = 0; index < ordered.Length - 1; index++)
        {
            double from = ordered[index];
            double to = ordered[index + 1];
            if (to - from <= 1e-12)
            {
                continue;
            }
            int unwrappedQuadrant = (int)Math.Floor(from);
            int quadrant = ((unwrappedQuadrant % 4) + 4) % 4;
            VectorCubic segment = Subcurve(quadrants[quadrant], from - unwrappedQuadrant, to - unwrappedQuadrant);
            if (segments.Count > 0 && segments[^1] is VectorCubic previous)
            {
                segment = segment with { P0 = previous.P3 };
            }
            if (Math.Abs(to - (targetStartParameter + 4d)) <= 1e-12
                && segments.Count > 0
                && segments[0] is VectorCubic first)
            {
                segment = segment with { P3 = first.P0 };
            }
            segments.Add(segment);

            double midpoint = (from + to) / 2d;
            double toothPhase = (midpoint - targetStartParameter) / pitch;
            bool isTarget = toothPhase - Math.Floor(toothPhase) < targetFraction;
            if (isTarget && targetStart is null)
            {
                targetStart = segments.Count - 1;
            }
            if (!isTarget && targetStart is int start)
            {
                targets.Add(new RadialPatternTarget(start, segments.Count - 1 - start));
                targetStart = null;
            }
        }
        if (targetStart is int finalStart)
        {
            targets.Add(new RadialPatternTarget(finalStart, segments.Count - finalStart));
        }
        return new RadialPatternTargetLayout(
            new VectorShape([new VectorContour(segments, VectorContourRole.Outer)]),
            targets);
    }

    private static VectorCubic[] CircleCubics(CircleProfileShape circle, double startAngle)
    {
        VectorPoint Map(double x, double y)
        {
            double cosine = Math.Cos(startAngle);
            double sine = Math.Sin(startAngle);
            return new VectorPoint(
                circle.CenterX + (x * cosine) - (y * sine),
                circle.CenterY + (x * sine) + (y * cosine));
        }

        double radius = circle.Radius;
        double control = radius * CircleControl;
        VectorPoint p0 = Map(radius, 0);
        VectorPoint p1 = Map(0, -radius);
        VectorPoint p2 = Map(-radius, 0);
        VectorPoint p3 = Map(0, radius);
        return
        [
            new VectorCubic(p0, Map(radius, -control), Map(control, -radius), p1),
            new VectorCubic(p1, Map(-control, -radius), Map(-radius, -control), p2),
            new VectorCubic(p2, Map(-radius, control), Map(-control, radius), p3),
            new VectorCubic(p3, Map(control, radius), Map(radius, control), p0),
        ];
    }

    private static VectorCubic Subcurve(VectorCubic curve, double from, double to)
    {
        VectorCubic afterStart = from <= 0 ? curve : Split(curve, from).Right;
        double relativeEnd = from <= 0 ? to : (to - from) / (1d - from);
        return relativeEnd >= 1 ? afterStart : Split(afterStart, relativeEnd).Left;
    }

    private static (VectorCubic Left, VectorCubic Right) Split(VectorCubic curve, double t)
    {
        VectorPoint p01 = Lerp(curve.P0, curve.P1, t);
        VectorPoint p12 = Lerp(curve.P1, curve.P2, t);
        VectorPoint p23 = Lerp(curve.P2, curve.P3, t);
        VectorPoint p012 = Lerp(p01, p12, t);
        VectorPoint p123 = Lerp(p12, p23, t);
        VectorPoint point = Lerp(p012, p123, t);
        return (
            new VectorCubic(curve.P0, p01, p012, point),
            new VectorCubic(point, p123, p23, curve.P3));
    }

    private static VectorPoint Lerp(VectorPoint left, VectorPoint right, double t)
        => new(
            left.X + ((right.X - left.X) * t),
            left.Y + ((right.Y - left.Y) * t));

    public static IReadOnlyList<ProfileReplacementSegment> InstantiatePattern(
        ProfileSpanPattern pattern,
        VectorPoint targetStart,
        VectorPoint targetEnd,
        VectorPoint? orientationTangent = null)
    {
        double dx = targetEnd.X - targetStart.X;
        double dy = targetEnd.Y - targetStart.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length == 0)
        {
            throw new ProfileResolutionException("COPE-PROFILE-0053", "A span pattern cannot target a zero-length boundary span.", ProfileSourceSpan.Generated());
        }

        VectorPoint Map(VectorPoint local)
        {
            if (local == new VectorPoint(0, 0))
            {
                return targetStart;
            }
            if (local == new VectorPoint(1, 0))
            {
                return targetEnd;
            }
            VectorPoint tangent = orientationTangent ?? new VectorPoint(dx / length, dy / length);
            double normalX = -tangent.Y;
            double normalY = tangent.X;
            return new VectorPoint(
                targetStart.X + (local.X * dx) + (local.Y * normalX),
                targetStart.Y + (local.X * dy) + (local.Y * normalY));
        }

        return pattern.Segments.Select(segment => new ProfileReplacementSegment(
            segment.Kind,
            Map(segment.Start),
            Map(segment.End),
            segment.Amount,
            Map(segment.Control1),
            Map(segment.Control2))).ToArray();
    }

    public static (VectorPoint Start, VectorPoint End) SpanEndpoints(
        VectorShape source,
        int startSegmentIndex,
        int segmentCount)
    {
        VectorContour outer = source.Contours.First(contour => contour.Role != VectorContourRole.Hole);
        if (startSegmentIndex < 0 || segmentCount <= 0 || startSegmentIndex + segmentCount > outer.Segments.Count)
        {
            throw new ProfileResolutionException("COPE-PROFILE-0042", "Span pattern target is outside the profile boundary.", ProfileSourceSpan.Generated());
        }
        return (Start(outer.Segments[startSegmentIndex]), End(outer.Segments[startSegmentIndex + segmentCount - 1]));
    }

    public static double SpanLength(VectorShape source, int startSegmentIndex, int segmentCount)
    {
        VectorContour outer = source.Contours.First(contour => contour.Role != VectorContourRole.Hole);
        return outer.Segments
            .Skip(startSegmentIndex)
            .Take(segmentCount)
            .Sum(ArcLength);
    }

    public static double ConceptPathLength(ProfileConceptPath path)
        => ArcLength(CreateReplacementSegment(path.Segment));

    public static bool ConceptPathHasCusp(ProfileConceptPath path)
    {
        VectorSegment segment = CreateReplacementSegment(path.Segment);
        for (int index = 0; index <= 256; index++)
        {
            double t = index / 256d;
            VectorPoint tangent = Derivative(segment, t);
            if ((tangent.X * tangent.X) + (tangent.Y * tangent.Y) <= 1e-16)
            {
                return true;
            }
        }
        return false;
    }

    public static VectorPoint ConceptPathTangent(ProfileConceptPath path, double distance)
    {
        VectorSegment segment = CreateReplacementSegment(path.Segment);
        double parameter = ParameterAtLength(segment, distance);
        VectorPoint tangent = Derivative(segment, parameter);
        double length = Math.Sqrt((tangent.X * tangent.X) + (tangent.Y * tangent.Y));
        if (length <= 1e-12)
        {
            throw new ProfileResolutionException("COPE-PROFILE-0063", "RepeatAlongPath encountered a cusp with no usable tangent.", ProfileSourceSpan.Generated());
        }
        return new VectorPoint(tangent.X / length, tangent.Y / length);
    }

    public static RefinedSpanInterval RefineSpanInterval(
        VectorShape source,
        int startSegmentIndex,
        int segmentCount,
        double startFraction,
        double endFraction)
    {
        VectorContour outer = source.Contours.First(contour => contour.Role != VectorContourRole.Hole);
        VectorSegment[] selected = outer.Segments.Skip(startSegmentIndex).Take(segmentCount).ToArray();
        double[] lengths = selected.Select(ArcLength).ToArray();
        double totalLength = lengths.Sum();
        if (totalLength <= 1e-12)
        {
            throw new ProfileResolutionException("COPE-PROFILE-0063", "Selector resolves to a zero-length path.", ProfileSourceSpan.Generated());
        }

        double fromDistance = totalLength * startFraction;
        double toDistance = totalLength * endFraction;
        var replacement = new List<VectorSegment>();
        var sourceIndexes = new List<int>();
        int refinedStart = -1;
        int refinedCount = 0;
        double cursor = 0;
        for (int localIndex = 0; localIndex < selected.Length; localIndex++)
        {
            VectorSegment segment = selected[localIndex];
            double length = lengths[localIndex];
            var cuts = new List<double> { 0, 1 };
            if (fromDistance > cursor + 1e-12 && fromDistance < cursor + length - 1e-12)
            {
                cuts.Add(ParameterAtLength(segment, fromDistance - cursor));
            }
            if (toDistance > cursor + 1e-12 && toDistance < cursor + length - 1e-12)
            {
                cuts.Add(ParameterAtLength(segment, toDistance - cursor));
            }
            double[] orderedCuts = cuts.Distinct().Order().ToArray();
            for (int cutIndex = 0; cutIndex < orderedCuts.Length - 1; cutIndex++)
            {
                double pieceStartDistance = cursor + ArcLength(Subsegment(segment, 0, orderedCuts[cutIndex]));
                double pieceEndDistance = cursor + ArcLength(Subsegment(segment, 0, orderedCuts[cutIndex + 1]));
                VectorSegment piece = Subsegment(segment, orderedCuts[cutIndex], orderedCuts[cutIndex + 1]);
                if (replacement.Count > 0)
                {
                    piece = SetStart(piece, End(replacement[^1]));
                }
                replacement.Add(piece);
                sourceIndexes.Add(startSegmentIndex + localIndex);
                bool inTarget = pieceStartDistance >= fromDistance - 1e-8
                    && pieceEndDistance <= toDistance + 1e-8;
                if (inTarget)
                {
                    if (refinedStart < 0)
                    {
                        refinedStart = startSegmentIndex + replacement.Count - 1;
                    }
                    refinedCount++;
                }
            }
            cursor += length;
        }

        VectorSegment[] outerSegments = outer.Segments
            .Take(startSegmentIndex)
            .Concat(replacement)
            .Concat(outer.Segments.Skip(startSegmentIndex + segmentCount))
            .ToArray();
        var refinedOuter = new VectorContour(outerSegments, outer.Role);
        VectorContour[] contours = source.Contours
            .Select(contour => ReferenceEquals(contour, outer) ? refinedOuter : contour)
            .ToArray();
        return new RefinedSpanInterval(
            new VectorShape(contours, source.FillRule),
            refinedStart,
            refinedCount,
            sourceIndexes);
    }

    private static double ArcLength(VectorSegment segment)
    {
        const int steps = 96;
        double length = 0;
        VectorPoint previous = Evaluate(segment, 0);
        for (int index = 1; index <= steps; index++)
        {
            VectorPoint point = Evaluate(segment, index / (double)steps);
            length += Distance(previous, point);
            previous = point;
        }
        return length;
    }

    private static double ParameterAtLength(VectorSegment segment, double requestedLength)
    {
        double total = ArcLength(segment);
        double low = 0;
        double high = 1;
        for (int iteration = 0; iteration < 48; iteration++)
        {
            double middle = (low + high) / 2d;
            double prefix = ArcLength(Subsegment(segment, 0, middle));
            if (prefix < requestedLength)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }
        return requestedLength <= 0 ? 0 : requestedLength >= total ? 1 : (low + high) / 2d;
    }

    private static VectorSegment Subsegment(VectorSegment segment, double from, double to)
    {
        if (segment is VectorLine line)
        {
            return new VectorLine(Evaluate(line, from), Evaluate(line, to));
        }
        if (segment is VectorQuadratic quadratic)
        {
            VectorQuadratic afterStart = from <= 0 ? quadratic : Split(quadratic, from).Right;
            double relativeEnd = from <= 0 ? to : (to - from) / (1d - from);
            return relativeEnd >= 1 ? afterStart : Split(afterStart, relativeEnd).Left;
        }
        if (segment is VectorCubic cubic)
        {
            return Subcurve(cubic, from, to);
        }
        throw new InvalidOperationException();
    }

    private static VectorSegment SetStart(VectorSegment segment, VectorPoint start)
    {
        return segment switch
        {
            VectorLine line => line with { P0 = start },
            VectorQuadratic quadratic => quadratic with { P0 = start },
            VectorCubic cubic => cubic with { P0 = start },
            _ => throw new InvalidOperationException(),
        };
    }

    private static (VectorQuadratic Left, VectorQuadratic Right) Split(VectorQuadratic curve, double t)
    {
        VectorPoint p01 = Lerp(curve.P0, curve.P1, t);
        VectorPoint p12 = Lerp(curve.P1, curve.P2, t);
        VectorPoint point = Lerp(p01, p12, t);
        return (
            new VectorQuadratic(curve.P0, p01, point),
            new VectorQuadratic(point, p12, curve.P2));
    }

    private static VectorPoint Evaluate(VectorSegment segment, double t)
    {
        double s = 1d - t;
        return segment switch
        {
            VectorLine line => Lerp(line.P0, line.P1, t),
            VectorQuadratic quadratic => new VectorPoint(
                (s * s * quadratic.P0.X) + (2 * s * t * quadratic.P1.X) + (t * t * quadratic.P2.X),
                (s * s * quadratic.P0.Y) + (2 * s * t * quadratic.P1.Y) + (t * t * quadratic.P2.Y)),
            VectorCubic cubic => new VectorPoint(
                (s * s * s * cubic.P0.X) + (3 * s * s * t * cubic.P1.X) + (3 * s * t * t * cubic.P2.X) + (t * t * t * cubic.P3.X),
                (s * s * s * cubic.P0.Y) + (3 * s * s * t * cubic.P1.Y) + (3 * s * t * t * cubic.P2.Y) + (t * t * t * cubic.P3.Y)),
            _ => throw new InvalidOperationException(),
        };
    }

    private static VectorPoint Derivative(VectorSegment segment, double t)
    {
        double s = 1d - t;
        return segment switch
        {
            VectorLine line => new VectorPoint(line.P1.X - line.P0.X, line.P1.Y - line.P0.Y),
            VectorQuadratic quadratic => new VectorPoint(
                (2 * s * (quadratic.P1.X - quadratic.P0.X)) + (2 * t * (quadratic.P2.X - quadratic.P1.X)),
                (2 * s * (quadratic.P1.Y - quadratic.P0.Y)) + (2 * t * (quadratic.P2.Y - quadratic.P1.Y))),
            VectorCubic cubic => new VectorPoint(
                (3 * s * s * (cubic.P1.X - cubic.P0.X)) + (6 * s * t * (cubic.P2.X - cubic.P1.X)) + (3 * t * t * (cubic.P3.X - cubic.P2.X)),
                (3 * s * s * (cubic.P1.Y - cubic.P0.Y)) + (6 * s * t * (cubic.P2.Y - cubic.P1.Y)) + (3 * t * t * (cubic.P3.Y - cubic.P2.Y))),
            _ => throw new InvalidOperationException(),
        };
    }

    private static double Distance(VectorPoint left, VectorPoint right)
    {
        double dx = right.X - left.X;
        double dy = right.Y - left.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public static VectorShape EdgeFeatures(
        RoundedRectangleProfileShape rectangle,
        IReadOnlyList<TabProfileOperation> tabs,
        IReadOnlyList<NotchProfileOperation> notches,
        IReadOnlyList<VectorContour> holes)
    {
        double left = -rectangle.Width / 2d;
        double right = rectangle.Width / 2d;
        double bottom = -rectangle.Height / 2d;
        double top = rectangle.Height / 2d;
        double radius = rectangle.Radius;
        double control = radius * CircleControl;
        VectorPoint topLeft = new(left + radius, top);
        VectorPoint topRight = new(right - radius, top);
        VectorPoint rightTop = new(right, top - radius);
        VectorPoint rightBottom = new(right, bottom + radius);
        VectorPoint bottomRight = new(right - radius, bottom);
        VectorPoint bottomLeft = new(left + radius, bottom);
        VectorPoint leftBottom = new(left, bottom + radius);
        VectorPoint leftTop = new(left, top - radius);
        List<VectorSegment> segments = [];

        List<VectorPoint> topPoints = [topLeft];
        AddHorizontalFeatures(topPoints, topLeft.X, topRight.X, top, tabs, notches, ProfileEdge.Top, outward: 1);
        AddLines(segments, topPoints);
        segments.Add(new VectorCubic(topRight, new(right - radius + control, top), new(right, top - radius + control), rightTop));

        List<VectorPoint> rightPoints = [rightTop];
        AddVerticalFeatures(rightPoints, rightTop.Y, rightBottom.Y, right, tabs, notches, ProfileEdge.Right, outward: 1);
        AddLines(segments, rightPoints);
        segments.Add(new VectorCubic(rightBottom, new(right, bottom + radius - control), new(right - radius + control, bottom), bottomRight));

        List<VectorPoint> bottomPoints = [bottomRight];
        AddHorizontalFeatures(bottomPoints, bottomRight.X, bottomLeft.X, bottom, tabs, notches, ProfileEdge.Bottom, outward: -1);
        AddLines(segments, bottomPoints);
        segments.Add(new VectorCubic(bottomLeft, new(left + radius - control, bottom), new(left, bottom + radius - control), leftBottom));

        List<VectorPoint> leftPoints = [leftBottom];
        AddVerticalFeatures(leftPoints, leftBottom.Y, leftTop.Y, left, tabs, notches, ProfileEdge.Left, outward: -1);
        AddLines(segments, leftPoints);
        segments.Add(new VectorCubic(leftTop, new(left, top - radius + control), new(left + radius - control, top), topLeft));

        VectorContour outline = new(segments, VectorContourRole.Outer);
        return new VectorShape(new[] { outline }.Concat(holes).ToArray());
    }

    public static VectorShape Transform(VectorShape source, string kind, double a, double b)
    {
        Func<VectorPoint, VectorPoint> transform = kind switch
        {
            "Translate" => point => new VectorPoint(point.X + a, point.Y + b),
            "Rotate" => RotateTransform(a),
            "Scale" => point => new VectorPoint(point.X * a, point.Y * b),
            "Mirror" when a == 1 => point => new VectorPoint(-point.X, point.Y),
            "Mirror" => point => new VectorPoint(point.X, -point.Y),
            _ => throw new InvalidOperationException($"Unknown transform '{kind}'."),
        };
        return new VectorShape(source.Contours.Select(contour => new VectorContour(
            contour.Segments.Select(segment => Transform(segment, transform)).ToArray())).ToArray());
    }

    public static VectorShape ReplaceSegment(VectorShape source, int segmentIndex, SegmentReplacement replacement)
    {
        VectorContour outer = source.Contours.First(contour => contour.Role != VectorContourRole.Hole);
        if (segmentIndex < 0 || segmentIndex >= outer.Segments.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentIndex), $"Segment index {segmentIndex} is outside the boundary.");
        }

        VectorSegment selected = outer.Segments[segmentIndex];
        VectorPoint start = Start(selected);
        VectorPoint end = End(selected);
        VectorSegment replacementSegment = replacement.Kind switch
        {
            ProfileCurveKind.Arc => BulgeSegment(start, end, replacement.Amount),
            ProfileCurveKind.Bulge => BulgeSegment(start, end, replacement.Amount),
            ProfileCurveKind.Spline => new VectorCubic(start, replacement.Control1, replacement.Control2, end),
            _ => throw new InvalidOperationException($"Unknown replacement curve '{replacement.Kind}'."),
        };

        return ReplaceSpan(source, segmentIndex, 1, [replacementSegment]);
    }

    public static VectorShape ReplaceSpan(
        VectorShape source,
        int startSegmentIndex,
        int segmentCount,
        IReadOnlyList<VectorSegment> replacements)
    {
        VectorContour outer = source.Contours.First(contour => contour.Role != VectorContourRole.Hole);
        if (segmentCount <= 0 || replacements.Count == 0)
        {
            throw new ProfileResolutionException("COPE-PROFILE-0044", "ReplaceSpan target and replacement must be non-empty.", ProfileSourceSpan.Generated());
        }
        if (startSegmentIndex < 0 || startSegmentIndex + segmentCount > outer.Segments.Count)
        {
            throw new ProfileResolutionException("COPE-PROFILE-0042", "ReplaceSpan target is outside the profile boundary.", ProfileSourceSpan.Generated());
        }
        if (segmentCount == outer.Segments.Count)
        {
            throw new ProfileResolutionException("COPE-PROFILE-0048", "Replacing the entire boundary is not supported in M4.", ProfileSourceSpan.Generated());
        }

        VectorPoint targetStart = Start(outer.Segments[startSegmentIndex]);
        VectorPoint targetEnd = End(outer.Segments[startSegmentIndex + segmentCount - 1]);
        if (Start(replacements[0]) != targetStart || End(replacements[^1]) != targetEnd)
        {
            throw new ProfileResolutionException("COPE-PROFILE-0045", "Replacement span endpoints must exactly match the target traversal endpoints; reversed spans are rejected.", ProfileSourceSpan.Generated());
        }
        for (int index = 1; index < replacements.Count; index++)
        {
            if (End(replacements[index - 1]) != Start(replacements[index]))
            {
                throw new ProfileResolutionException("COPE-PROFILE-0046", $"Replacement span is disconnected between generated segments {index - 1} and {index}.", ProfileSourceSpan.Generated());
            }
        }

        VectorSegment[] segments = outer.Segments
            .Take(startSegmentIndex)
            .Concat(replacements)
            .Concat(outer.Segments.Skip(startSegmentIndex + segmentCount))
            .ToArray();
        var changed = new VectorContour(segments, outer.Role);
        VectorContour[] contours = source.Contours
            .Select(contour => ReferenceEquals(contour, outer) ? changed : contour)
            .ToArray();
        VectorShape result = new(contours, source.FillRule);
        ValidateSimpleBoundary(result, replacements);
        return result;
    }

    public static string ToSvg(VectorShape shape)
    {
        VectorBounds bounds = shape.Bounds;
        return FormattableString.Invariant(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{bounds.MinX:R} {-bounds.MaxY:R} {bounds.Width:R} {bounds.Height:R}\"><path fill=\"black\" fill-rule=\"nonzero\" d=\"{ToSvgPath(shape)}\"/></svg>");
    }

    internal static string ToSvgPath(VectorShape shape)
    {
        StringBuilder path = new();
        foreach (VectorContour contour in shape.Contours)
        {
            VectorPoint start = Start(contour.Segments[0]);
            path.Append("M ").Append(Number(start.X)).Append(' ').Append(Number(-start.Y)).Append(' ');
            foreach (VectorSegment segment in contour.Segments)
            {
                switch (segment)
                {
                    case VectorLine line:
                        path.Append("L ").Append(Number(line.P1.X)).Append(' ').Append(Number(-line.P1.Y)).Append(' ');
                        break;
                    case VectorQuadratic quadratic:
                        path.Append("Q ").Append(Number(quadratic.P1.X)).Append(' ').Append(Number(-quadratic.P1.Y)).Append(' ')
                            .Append(Number(quadratic.P2.X)).Append(' ').Append(Number(-quadratic.P2.Y)).Append(' ');
                        break;
                    case VectorCubic cubic:
                        path.Append("C ").Append(Number(cubic.P1.X)).Append(' ').Append(Number(-cubic.P1.Y)).Append(' ')
                            .Append(Number(cubic.P2.X)).Append(' ').Append(Number(-cubic.P2.Y)).Append(' ')
                            .Append(Number(cubic.P3.X)).Append(' ').Append(Number(-cubic.P3.Y)).Append(' ');
                        break;
                }
            }
            path.Append("Z ");
        }
        return path.ToString().TrimEnd();
    }

    private static VectorShape Rectangle(double width, double height)
    {
        double x = width / 2d;
        double y = height / 2d;
        return Polygon([new(-x, y), new(x, y), new(x, -y), new(-x, -y)]);
    }

    private static VectorShape RoundedRectangle(double width, double height, double radius)
    {
        double x = width / 2d;
        double y = height / 2d;
        double control = radius * CircleControl;
        VectorPoint p0 = new(-x + radius, y);
        VectorPoint p1 = new(x - radius, y);
        VectorPoint p2 = new(x, y - radius);
        VectorPoint p3 = new(x, -y + radius);
        VectorPoint p4 = new(x - radius, -y);
        VectorPoint p5 = new(-x + radius, -y);
        VectorPoint p6 = new(-x, -y + radius);
        VectorPoint p7 = new(-x, y - radius);
        return new VectorShape([new VectorContour([
            new VectorLine(p0, p1),
            new VectorCubic(p1, new(x - radius + control, y), new(x, y - radius + control), p2),
            new VectorLine(p2, p3),
            new VectorCubic(p3, new(x, -y + radius - control), new(x - radius + control, -y), p4),
            new VectorLine(p4, p5),
            new VectorCubic(p5, new(-x + radius - control, -y), new(-x, -y + radius - control), p6),
            new VectorLine(p6, p7),
            new VectorCubic(p7, new(-x, y - radius + control), new(-x + radius - control, y), p0),
        ])]);
    }

    private static VectorShape Ellipse(double radiusX, double radiusY, double centerX, double centerY)
    {
        double cx = radiusX * CircleControl;
        double cy = radiusY * CircleControl;
        VectorPoint top = new(centerX, centerY + radiusY);
        VectorPoint right = new(centerX + radiusX, centerY);
        VectorPoint bottom = new(centerX, centerY - radiusY);
        VectorPoint left = new(centerX - radiusX, centerY);
        return new VectorShape([new VectorContour([
            new VectorCubic(top, new(centerX + cx, centerY + radiusY), new(centerX + radiusX, centerY + cy), right),
            new VectorCubic(right, new(centerX + radiusX, centerY - cy), new(centerX + cx, centerY - radiusY), bottom),
            new VectorCubic(bottom, new(centerX - cx, centerY - radiusY), new(centerX - radiusX, centerY - cy), left),
            new VectorCubic(left, new(centerX - radiusX, centerY + cy), new(centerX - cx, centerY + radiusY), top),
        ])]);
    }

    private static VectorShape Slot(double length, double width, double angleDegrees, double centerX, double centerY)
    {
        double straightLength = Math.Max(0, length - width);
        VectorShape horizontal = Capsule(
            new VectorPoint(-straightLength / 2d, 0),
            new VectorPoint(straightLength / 2d, 0),
            width);
        VectorShape rotated = Transform(horizontal, "Rotate", angleDegrees, 0);
        return Transform(rotated, "Translate", centerX, centerY);
    }

    private static VectorShape Capsule(VectorPoint from, VectorPoint to, double width)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length == 0)
        {
            return Ellipse(width / 2d, width / 2d, from.X, from.Y);
        }

        double radius = width / 2d;
        double nx = -dy / length;
        double ny = dx / length;
        VectorPoint aTop = new(from.X + (nx * radius), from.Y + (ny * radius));
        VectorPoint bTop = new(to.X + (nx * radius), to.Y + (ny * radius));
        VectorPoint bBottom = new(to.X - (nx * radius), to.Y - (ny * radius));
        VectorPoint aBottom = new(from.X - (nx * radius), from.Y - (ny * radius));
        double tangentX = dx / length * radius * CircleControl;
        double tangentY = dy / length * radius * CircleControl;
        return new VectorShape([new VectorContour([
            new VectorLine(aTop, bTop),
            new VectorCubic(bTop, new(bTop.X + tangentX, bTop.Y + tangentY), new(bBottom.X + tangentX, bBottom.Y + tangentY), bBottom),
            new VectorLine(bBottom, aBottom),
            new VectorCubic(aBottom, new(aBottom.X - tangentX, aBottom.Y - tangentY), new(aTop.X - tangentX, aTop.Y - tangentY), aTop),
        ])]);
    }

    private static VectorSegment BulgeSegment(VectorPoint start, VectorPoint end, double amount)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length == 0)
        {
            throw new ArgumentException("Cannot replace a zero-length boundary segment.");
        }
        VectorPoint control = new(
            ((start.X + end.X) / 2d) - (dy / length * amount * 2d),
            ((start.Y + end.Y) / 2d) + (dx / length * amount * 2d));
        return new VectorQuadratic(start, control, end);
    }

    internal static VectorSegment CreateReplacementSegment(ProfileReplacementSegment segment)
    {
        return segment.Kind switch
        {
            ProfileCurveKind.Line => new VectorLine(segment.Start, segment.End),
            ProfileCurveKind.Arc => BulgeSegment(segment.Start, segment.End, segment.Amount),
            ProfileCurveKind.Bulge => BulgeSegment(segment.Start, segment.End, segment.Amount),
            ProfileCurveKind.Spline => new VectorCubic(segment.Start, segment.Control1, segment.Control2, segment.End),
            _ => throw new InvalidOperationException($"Unknown replacement curve '{segment.Kind}'."),
        };
    }

    private static void ValidateSimpleBoundary(VectorShape shape, IReadOnlyList<VectorSegment> replacements)
    {
        VectorContour outer = shape.Contours.First(contour => contour.Role != VectorContourRole.Hole);
        IReadOnlyList<VectorPoint> replacementPoints = replacements
            .SelectMany((segment, index) => FlattenForValidation(segment).Skip(index == 0 ? 0 : 1))
            .ToArray();

        for (int left = 0; left < replacementPoints.Count - 1; left++)
        {
            for (int right = left + 2; right < replacementPoints.Count - 1; right++)
            {
                if (left == 0
                    && right == replacementPoints.Count - 2
                    && replacementPoints[0] == replacementPoints[^1])
                {
                    continue;
                }
                if (SegmentsIntersect(replacementPoints[left], replacementPoints[left + 1], replacementPoints[right], replacementPoints[right + 1]))
                {
                    throw new ProfileResolutionException("COPE-PROFILE-0043", "Replacement span self-intersects.", ProfileSourceSpan.Generated());
                }
            }
        }

        for (int index = 0; index < outer.Segments.Count; index++)
        {
            VectorSegment candidate = outer.Segments[index];
            if (replacements.Any(replacement => ReferenceEquals(candidate, replacement))
                || End(candidate) == replacementPoints[0]
                || Start(candidate) == replacementPoints[^1])
            {
                continue;
            }
            IReadOnlyList<VectorPoint> candidatePoints = FlattenForValidation(candidate);
            for (int left = 0; left < replacementPoints.Count - 1; left++)
            {
                for (int right = 0; right < candidatePoints.Count - 1; right++)
                {
                    if (SegmentsIntersect(replacementPoints[left], replacementPoints[left + 1], candidatePoints[right], candidatePoints[right + 1]))
                    {
                        throw new ProfileResolutionException("COPE-PROFILE-0043", "Segment replacement self-intersects the profile boundary.", ProfileSourceSpan.Generated());
                    }
                }
            }
        }
    }

    private static IReadOnlyList<VectorPoint> FlattenForValidation(VectorSegment segment)
    {
        var points = new List<VectorPoint>();
        for (int step = 0; step <= 32; step++)
        {
            double t = step / 32d;
            double s = 1d - t;
            points.Add(segment switch
            {
                VectorLine line => new VectorPoint((s * line.P0.X) + (t * line.P1.X), (s * line.P0.Y) + (t * line.P1.Y)),
                VectorQuadratic quadratic => new VectorPoint((s * s * quadratic.P0.X) + (2 * s * t * quadratic.P1.X) + (t * t * quadratic.P2.X), (s * s * quadratic.P0.Y) + (2 * s * t * quadratic.P1.Y) + (t * t * quadratic.P2.Y)),
                VectorCubic cubic => new VectorPoint((s * s * s * cubic.P0.X) + (3 * s * s * t * cubic.P1.X) + (3 * s * t * t * cubic.P2.X) + (t * t * t * cubic.P3.X), (s * s * s * cubic.P0.Y) + (3 * s * s * t * cubic.P1.Y) + (3 * s * t * t * cubic.P2.Y) + (t * t * t * cubic.P3.Y)),
                _ => throw new InvalidOperationException(),
            });
        }
        return points;
    }

    private static bool SegmentsIntersect(VectorPoint a, VectorPoint b, VectorPoint c, VectorPoint d)
    {
        double abC = Cross(a, b, c);
        double abD = Cross(a, b, d);
        double cdA = Cross(c, d, a);
        double cdB = Cross(c, d, b);
        return abC * abD < -1e-12 && cdA * cdB < -1e-12;
    }

    private static double Cross(VectorPoint a, VectorPoint b, VectorPoint c)
        => ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    private static VectorShape RegularPolygon(int sides, double radius, double rotationDegrees)
    {
        return Polygon(Enumerable.Range(0, sides).Select(index =>
        {
            double angle = Degrees(rotationDegrees) - (2d * Math.PI * index / sides);
            return new VectorPoint(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
        }).ToArray());
    }

    private static VectorShape Polygon(IReadOnlyList<VectorPoint> points)
    {
        return new VectorShape([PolygonContour(points)]);
    }

    private static VectorContour PolygonContour(IReadOnlyList<VectorPoint> points)
    {
        return new VectorContour(Enumerable.Range(0, points.Count)
            .Select(index => (VectorSegment)new VectorLine(points[index], points[(index + 1) % points.Count]))
            .ToArray());
    }

    private static void AddHorizontalFeatures(
        List<VectorPoint> points,
        double start,
        double end,
        double y,
        IReadOnlyList<TabProfileOperation> tabs,
        IReadOnlyList<NotchProfileOperation> notches,
        ProfileEdge edge,
        int outward)
    {
        double direction = Math.Sign(end - start);
        foreach ((double center, double width, double depth, bool tab) in Features(tabs, notches, edge, start, end))
        {
            double before = center - (direction * width / 2d);
            double after = center + (direction * width / 2d);
            double featureY = y + (outward * (tab ? depth : -depth));
            Add(points, before, y);
            Add(points, before, featureY);
            Add(points, after, featureY);
            Add(points, after, y);
        }
        Add(points, end, y);
    }

    private static void AddVerticalFeatures(
        List<VectorPoint> points,
        double start,
        double end,
        double x,
        IReadOnlyList<TabProfileOperation> tabs,
        IReadOnlyList<NotchProfileOperation> notches,
        ProfileEdge edge,
        int outward)
    {
        double direction = Math.Sign(end - start);
        foreach ((double center, double width, double depth, bool tab) in Features(tabs, notches, edge, start, end))
        {
            double before = center - (direction * width / 2d);
            double after = center + (direction * width / 2d);
            double featureX = x + (outward * (tab ? depth : -depth));
            Add(points, x, before);
            Add(points, featureX, before);
            Add(points, featureX, after);
            Add(points, x, after);
        }
        Add(points, x, end);
    }

    private static IEnumerable<(double Center, double Width, double Depth, bool Tab)> Features(
        IReadOnlyList<TabProfileOperation> tabs,
        IReadOnlyList<NotchProfileOperation> notches,
        ProfileEdge edge,
        double start,
        double end)
    {
        double length = Math.Abs(end - start);
        double direction = Math.Sign(end - start);
        return tabs.Where(item => item.Edge == edge)
            .Select(item => (start + (direction * item.Position * length), item.Width, item.Depth, true))
            .Concat(notches.Where(item => item.Edge == edge)
                .Select(item => (start + (direction * item.Position * length), item.Width, item.Depth, false)))
            .OrderBy(item => direction * item.Item1);
    }

    private static void Add(List<VectorPoint> points, double x, double y)
    {
        VectorPoint point = new(x, y);
        if (points.Count == 0 || points[^1] != point)
        {
            points.Add(point);
        }
    }

    private static void AddLines(List<VectorSegment> segments, IReadOnlyList<VectorPoint> points)
    {
        for (int index = 0; index < points.Count - 1; index++)
        {
            segments.Add(new VectorLine(points[index], points[index + 1]));
        }
    }

    public static VectorContour WithRole(VectorContour contour, VectorContourRole role)
    {
        return new VectorContour(contour.Segments, role);
    }

    private static bool Contains(VectorBounds outer, VectorBounds inner)
    {
        return inner.MinX > outer.MinX && inner.MaxX < outer.MaxX
            && inner.MinY > outer.MinY && inner.MaxY < outer.MaxY;
    }

    private static bool Intersects(VectorBounds left, VectorBounds right)
    {
        return left.MinX < right.MaxX && left.MaxX > right.MinX
            && left.MinY < right.MaxY && left.MaxY > right.MinY;
    }

    private static Func<VectorPoint, VectorPoint> RotateTransform(double degrees)
    {
        double angle = Degrees(degrees);
        double cosine = Math.Cos(angle);
        double sine = Math.Sin(angle);
        return point => new VectorPoint(
            (point.X * cosine) - (point.Y * sine),
            (point.X * sine) + (point.Y * cosine));
    }

    private static VectorSegment Transform(VectorSegment segment, Func<VectorPoint, VectorPoint> transform)
    {
        return segment switch
        {
            VectorLine line => new VectorLine(transform(line.P0), transform(line.P1)),
            VectorQuadratic quadratic => new VectorQuadratic(transform(quadratic.P0), transform(quadratic.P1), transform(quadratic.P2)),
            VectorCubic cubic => new VectorCubic(transform(cubic.P0), transform(cubic.P1), transform(cubic.P2), transform(cubic.P3)),
            _ => throw new InvalidOperationException(),
        };
    }

    private static VectorPoint Start(VectorSegment segment)
    {
        return segment switch
        {
            VectorLine line => line.P0,
            VectorQuadratic quadratic => quadratic.P0,
            VectorCubic cubic => cubic.P0,
            _ => throw new InvalidOperationException(),
        };
    }

    private static VectorPoint End(VectorSegment segment)
    {
        return segment switch
        {
            VectorLine line => line.P1,
            VectorQuadratic quadratic => quadratic.P2,
            VectorCubic cubic => cubic.P3,
            _ => throw new InvalidOperationException(),
        };
    }

    private static double Degrees(double value) => value * Math.PI / 180d;

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}

internal sealed class ProfileResolutionException : Exception
{
    public ProfileResolutionException(string id, string message, ProfileSourceSpan span) : base(message)
    {
        Id = id;
        Span = span;
    }

    public string Id { get; }

    public ProfileSourceSpan Span { get; }
}
