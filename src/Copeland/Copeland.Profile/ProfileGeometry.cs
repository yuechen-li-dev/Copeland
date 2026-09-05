using System.Globalization;
using System.Text;
namespace Copeland.Profile;

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
