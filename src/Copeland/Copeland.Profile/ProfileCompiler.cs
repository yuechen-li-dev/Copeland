using System.Globalization;
using System.Text;
namespace Copeland.Profile;

public static class ProfileCompiler
{
    public static ProfileCompilationResult Compile(ProfileDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        List<ProfileDiagnostic> diagnostics = ValidateDefinition(definition);
        if (diagnostics.Count > 0)
        {
            return Failure(definition, diagnostics);
        }

        Dictionary<string, WorkingState> states = new(StringComparer.Ordinal);
        List<ProfileStateSummary> summaries = [];
        try
        {
            VectorShape baseShape = ProfileGeometry.Create(definition.Base);
            WorkingState first = new(
                baseShape,
                definition.Base,
                [],
                [],
                [],
                [],
                Enumerable.Repeat("Base", SegmentCount(baseShape)).ToArray());
            states.Add(definition.BaseState, first);
            summaries.Add(Summary(0, definition.BaseState, null, definition.Base.Kind, first));

            foreach (ProfileOperation operation in definition.Operations)
            {
                if (!states.TryGetValue(operation.InputState, out WorkingState? input))
                {
                    diagnostics.Add(new ProfileDiagnostic(
                        "COPE-PROFILE-0010",
                        $"Operation '{operation.FeatureId}' references unknown prior state '{operation.InputState}'.",
                        operation.Span));
                    continue;
                }
                if (states.ContainsKey(operation.OutputState))
                {
                    diagnostics.Add(new ProfileDiagnostic(
                        "COPE-PROFILE-0011",
                        $"Profile state '{operation.OutputState}' is already defined.",
                        operation.Span));
                    continue;
                }

                WorkingState output = Apply(input, operation);
                states.Add(operation.OutputState, output);
                summaries.Add(Summary(
                    summaries.Count,
                    operation.OutputState,
                    operation.FeatureId,
                    operation.Kind,
                    output));
            }
        }
        catch (ProfileResolutionException ex)
        {
            diagnostics.Add(new ProfileDiagnostic(ex.Id, ex.Message, ex.Span));
        }
        catch (ArgumentException ex)
        {
            diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-0020", ex.Message, definition.Span));
        }

        if (diagnostics.Count > 0)
        {
            return Failure(definition, diagnostics, summaries);
        }
        if (!states.TryGetValue(definition.YieldState, out WorkingState? final))
        {
            diagnostics.Add(new ProfileDiagnostic(
                "COPE-PROFILE-0012",
                $"Yield references unknown profile state '{definition.YieldState}'.",
                definition.Span));
            return Failure(definition, diagnostics, summaries);
        }

        string irHash = ProfileHash.Utf8(CanonicalIr(definition));
        return new ProfileCompilationResult(
            definition,
            final.Shape,
            summaries,
            [],
            irHash,
            final.Shape.NormalizedGeometryHash,
            ProfileGeometry.ToSvg(final.Shape));
    }

    private static WorkingState Apply(WorkingState input, ProfileOperation operation)
    {
        switch (operation)
        {
            case AddProfileOperation add:
                return Changed(input, operation, ProfileGeometry.Add(input.Shape, add.Shape), clearBase: true);
            case SubtractProfileOperation subtract:
                return Changed(input, operation, ProfileGeometry.SubtractContained(input.Shape, subtract.Shape), clearBase: true);
            case HoleProfileOperation hole:
                return AddHole(input, hole);
            case TabProfileOperation tab:
                return AddEdgeFeature(input, tab, null);
            case NotchProfileOperation notch:
                return AddEdgeFeature(input, null, notch);
            case RepeatRadialProfileOperation repeat:
                return RepeatRadial(input, repeat);
            case ReplaceSegmentProfileOperation replace:
                return ReplaceSegment(input, replace);
            case TransformProfileOperation transform:
                return Changed(input, operation, ProfileGeometry.Transform(input.Shape, transform.TransformKind, transform.A, transform.B), clearBase: true);
            default:
                throw new InvalidOperationException($"Unknown profile operation '{operation.Kind}'.");
        }
    }

    private static WorkingState ReplaceSegment(WorkingState input, ReplaceSegmentProfileOperation operation)
    {
        VectorShape shape;
        try
        {
            shape = ProfileGeometry.ReplaceSegment(input.Shape, operation.SegmentIndex, operation.Replacement);
        }
        catch (ProfileResolutionException exception)
        {
            throw new ProfileResolutionException(exception.Id, $"Feature '{operation.FeatureId}' targeting segment '{operation.SegmentIndex}': {exception.Message}", operation.Span);
        }
        string[] provenance = input.SegmentProvenance.ToArray();
        if (operation.SegmentIndex < provenance.Length)
        {
            provenance[operation.SegmentIndex] = operation.FeatureId;
        }
        return input with
        {
            Shape = shape,
            Base = null,
            AppliedFeatureIds = input.AppliedFeatureIds.Append(operation.FeatureId).ToArray(),
            SegmentProvenance = provenance,
        };
    }

    private static WorkingState Changed(WorkingState input, ProfileOperation operation, VectorShape shape, bool clearBase)
    {
        return input with
        {
            Shape = shape,
            Base = clearBase ? null : input.Base,
            AppliedFeatureIds = input.AppliedFeatureIds.Append(operation.FeatureId).ToArray(),
            SegmentProvenance = Enumerable.Repeat(operation.FeatureId, SegmentCount(shape)).ToArray(),
        };
    }

    private static WorkingState AddHole(WorkingState input, HoleProfileOperation operation)
    {
        VectorShape holeShape = ProfileGeometry.Create(operation.Hole);
        if (!Contains(input.Shape.Bounds, holeShape.Bounds))
        {
            throw new ProfileResolutionException(
                "COPE-PROFILE-0031",
                $"Hole '{operation.FeatureId}' does not lie fully inside the current profile.",
                operation.Span);
        }
        VectorContour[] holes = input.Holes
            .Concat(holeShape.Contours.Select(contour => ProfileGeometry.WithRole(contour, VectorContourRole.Hole)))
            .ToArray();
        if (input.Base is RoundedRectangleProfileShape rounded)
        {
            return input with
            {
                Shape = ProfileGeometry.EdgeFeatures(rounded, input.Tabs, input.Notches, holes),
                Holes = holes,
                AppliedFeatureIds = input.AppliedFeatureIds.Append(operation.FeatureId).ToArray(),
            };
        }
        if (input.Base is RectangleProfileShape rectangle)
        {
            RoundedRectangleProfileShape square = new(rectangle.Width, rectangle.Height, 0, rectangle.Span);
            return input with
            {
                Shape = ProfileGeometry.EdgeFeatures(square, input.Tabs, input.Notches, holes),
                Holes = holes,
                AppliedFeatureIds = input.AppliedFeatureIds.Append(operation.FeatureId).ToArray(),
            };
        }
        return input with
        {
            Shape = new VectorShape(input.Shape.Contours.Concat(holes.Skip(input.Holes.Count)).ToArray()),
            Holes = holes,
            AppliedFeatureIds = input.AppliedFeatureIds.Append(operation.FeatureId).ToArray(),
        };
    }

    private static WorkingState AddEdgeFeature(
        WorkingState input,
        TabProfileOperation? tab,
        NotchProfileOperation? notch)
    {
        RoundedRectangleProfileShape rectangle = input.Base switch
        {
            RoundedRectangleProfileShape rounded => rounded,
            RectangleProfileShape plain => new RoundedRectangleProfileShape(plain.Width, plain.Height, 0, plain.Span),
            _ => throw new ProfileResolutionException(
                "COPE-PROFILE-0032",
                "M0 edge-relative tabs and notches require an axis-aligned rectangle base.",
                tab?.Span ?? notch!.Span),
        };
        TabProfileOperation[] tabs = tab is null ? input.Tabs.ToArray() : input.Tabs.Append(tab).ToArray();
        NotchProfileOperation[] notches = notch is null ? input.Notches.ToArray() : input.Notches.Append(notch).ToArray();
        ValidateEdgeFeatures(rectangle, tabs, notches, tab?.Span ?? notch!.Span);
        return input with
        {
            Shape = ProfileGeometry.EdgeFeatures(rectangle, tabs, notches, input.Holes),
            Tabs = tabs,
            Notches = notches,
            AppliedFeatureIds = input.AppliedFeatureIds.Append((tab as ProfileOperation ?? notch!).FeatureId).ToArray(),
        };
    }

    private static WorkingState RepeatRadial(WorkingState input, RepeatRadialProfileOperation operation)
    {
        if (input.Base is not CircleProfileShape circle || input.Tabs.Count > 0 || input.Notches.Count > 0)
        {
            throw new ProfileResolutionException(
                "COPE-PROFILE-0033",
                "M0 RepeatRadial teeth require an untransformed Circle base.",
                operation.Span);
        }
        VectorShape outline = ProfileGeometry.Gear(
            circle,
            operation.Count,
            operation.ToothDepth,
            operation.ToothFraction,
            operation.RotationDegrees);
        VectorShape shape = input.Holes.Count == 0
            ? outline
            : new VectorShape(outline.Contours.Concat(input.Holes).ToArray());
        return input with
        {
            Shape = shape,
            AppliedFeatureIds = input.AppliedFeatureIds.Append(operation.FeatureId).ToArray(),
        };
    }

    private static List<ProfileDiagnostic> ValidateDefinition(ProfileDefinition definition)
    {
        List<ProfileDiagnostic> diagnostics = [];
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-0001", "Profile name must not be empty.", definition.Span));
        }
        if (string.IsNullOrWhiteSpace(definition.BaseState))
        {
            diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-0002", "Base state must be named.", definition.Span));
        }
        if (string.IsNullOrWhiteSpace(definition.YieldState))
        {
            diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-0003", "Profile has no final yield state.", definition.Span));
        }
        ValidateShape(definition.Base, diagnostics);
        HashSet<string> featureIds = new(StringComparer.Ordinal);
        foreach (ProfileOperation operation in definition.Operations)
        {
            if (!featureIds.Add(operation.FeatureId))
            {
                diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-0013", $"Duplicate feature id '{operation.FeatureId}'.", operation.Span));
            }
            switch (operation)
            {
                case AddProfileOperation add:
                    ValidateShape(add.Shape, diagnostics);
                    break;
                case SubtractProfileOperation subtract:
                    ValidateShape(subtract.Shape, diagnostics);
                    break;
                case HoleProfileOperation hole:
                    ValidateShape(hole.Hole, diagnostics);
                    break;
                case TabProfileOperation tab:
                    ValidateEdgeOperation(tab.Width, tab.Depth, tab.Position, tab.Span, diagnostics);
                    ValidateEdge(tab.Edge, tab.Span, diagnostics);
                    break;
                case NotchProfileOperation notch:
                    ValidateEdgeOperation(notch.Width, notch.Depth, notch.Position, notch.Span, diagnostics);
                    ValidateEdge(notch.Edge, notch.Span, diagnostics);
                    break;
                case RepeatRadialProfileOperation repeat:
                    if (repeat.Count < 3 || repeat.Count > 256 || !Positive(repeat.ToothDepth)
                        || !double.IsFinite(repeat.ToothFraction) || repeat.ToothFraction <= 0 || repeat.ToothFraction >= 1)
                    {
                        diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-0024", "RepeatRadial requires count 3..256, positive toothDepth, and toothFraction between zero and one.", repeat.Span));
                    }
                    break;
                case ReplaceSegmentProfileOperation replace:
                    if (replace.SegmentIndex < 0
                        || !double.IsFinite(replace.Replacement.Amount)
                        || replace.Replacement.Kind == ProfileCurveKind.Spline
                            && (!Finite(replace.Replacement.Control1) || !Finite(replace.Replacement.Control2)))
                    {
                        diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-0041", "ReplaceSegment requires a non-negative selector and finite curve parameters.", replace.Span));
                    }
                    break;
                case TransformProfileOperation transform:
                    if (!double.IsFinite(transform.A) || !double.IsFinite(transform.B)
                        || transform.TransformKind == "Scale" && (transform.A == 0 || transform.B == 0))
                    {
                        diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-0025", $"Invalid {transform.TransformKind} parameters.", transform.Span));
                    }
                    break;
            }
        }
        return diagnostics;
    }

    private static void ValidateShape(ProfileShapeSpec shape, List<ProfileDiagnostic> diagnostics)
    {
        bool valid = shape switch
        {
            RectangleProfileShape rectangle => Positive(rectangle.Width) && Positive(rectangle.Height),
            RoundedRectangleProfileShape rounded => Positive(rounded.Width) && Positive(rounded.Height)
                && double.IsFinite(rounded.Radius) && rounded.Radius >= 0
                && rounded.Radius <= Math.Min(rounded.Width, rounded.Height) / 2d,
            CircleProfileShape circle => Positive(circle.Radius) && double.IsFinite(circle.CenterX) && double.IsFinite(circle.CenterY),
            EllipseProfileShape ellipse => Positive(ellipse.RadiusX) && Positive(ellipse.RadiusY)
                && double.IsFinite(ellipse.CenterX) && double.IsFinite(ellipse.CenterY),
            RegularPolygonProfileShape polygon => polygon.Sides >= 3 && polygon.Sides <= 1024 && Positive(polygon.Radius),
            PolygonProfileShape polygon => polygon.Points.Count >= 3 && polygon.Points.All(Finite),
            SlotProfileShape slot => Positive(slot.Length) && Positive(slot.Width) && slot.Length >= slot.Width
                && double.IsFinite(slot.AngleDegrees) && double.IsFinite(slot.CenterX) && double.IsFinite(slot.CenterY),
            CapsuleProfileShape capsule => Positive(capsule.Width) && Finite(capsule.From) && Finite(capsule.To),
            _ => false,
        };
        if (!valid)
        {
            diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-0020", $"Invalid {shape.Kind} dimensions.", shape.Span));
        }
    }

    private static void ValidateEdgeOperation(
        double width,
        double depth,
        double position,
        ProfileSourceSpan span,
        List<ProfileDiagnostic> diagnostics)
    {
        if (!Positive(width) || !Positive(depth) || !double.IsFinite(position) || position <= 0 || position >= 1)
        {
            diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-0023", "Tab and Notch require positive dimensions and a position between zero and one.", span));
        }
    }

    private static void ValidateEdge(
        ProfileEdge edge,
        ProfileSourceSpan span,
        List<ProfileDiagnostic> diagnostics)
    {
        if (!Enum.IsDefined(edge))
        {
            diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-0026", "Tab and Notch require Top, Right, Bottom, or Left.", span));
        }
    }

    private static void ValidateEdgeFeatures(
        RoundedRectangleProfileShape rectangle,
        IReadOnlyList<TabProfileOperation> tabs,
        IReadOnlyList<NotchProfileOperation> notches,
        ProfileSourceSpan span)
    {
        foreach ((ProfileEdge edge, double width, double position) in tabs
            .Select(item => (item.Edge, item.Width, item.Position))
            .Concat(notches.Select(item => (item.Edge, item.Width, item.Position))))
        {
            double available = edge is ProfileEdge.Top or ProfileEdge.Bottom
                ? rectangle.Width - (2d * rectangle.Radius)
                : rectangle.Height - (2d * rectangle.Radius);
            double start = (position * available) - (width / 2d);
            double end = start + width;
            if (start < 0 || end > available)
            {
                throw new ProfileResolutionException("COPE-PROFILE-0034", $"{edge} edge feature does not fit between the corners.", span);
            }
        }
    }

    private static ProfileStateSummary Summary(
        int index,
        string name,
        string? featureId,
        string operationKind,
        WorkingState state)
    {
        return new ProfileStateSummary(
            index,
            name,
            featureId,
            operationKind,
            state.AppliedFeatureIds,
            state.Shape.Contours.Count,
            state.Shape.Bounds,
            state.Shape.NormalizedGeometryHash)
        {
            Segments = state.Shape.Contours
                .SelectMany((contour, contourIndex) => contour.Segments.Select((segment, segmentIndex) => (contourIndex, segmentIndex, segment)))
                .Select((item, flatIndex) => new ProfileSegmentSummary(
                    $"contour:{item.contourIndex}/segment:{item.segmentIndex}",
                    SegmentHash(item.segment),
                    flatIndex < state.SegmentProvenance.Count ? state.SegmentProvenance[flatIndex] : featureId ?? "Base"))
                .ToArray(),
        };
    }

    private static string CanonicalIr(ProfileDefinition definition)
    {
        StringBuilder result = new();
        result.Append("profile-ir-v1|").Append(definition.Name).Append('|')
            .Append(definition.BaseState).Append('|').Append(Shape(definition.Base)).Append('|');
        foreach (ProfileOperation operation in definition.Operations)
        {
            result.Append(operation.FeatureId).Append('|').Append(operation.InputState).Append('|')
                .Append(operation.OutputState).Append('|').Append(operation.Kind).Append('|');
            switch (operation)
            {
                case AddProfileOperation add:
                    result.Append(Shape(add.Shape));
                    break;
                case SubtractProfileOperation subtract:
                    result.Append(Shape(subtract.Shape));
                    break;
                case HoleProfileOperation hole:
                    result.Append(Shape(hole.Hole));
                    break;
                case TabProfileOperation tab:
                    result.Append($"{tab.Edge}:{R(tab.Width)}:{R(tab.Depth)}:{R(tab.Position)}");
                    break;
                case NotchProfileOperation notch:
                    result.Append($"{notch.Edge}:{R(notch.Width)}:{R(notch.Depth)}:{R(notch.Position)}");
                    break;
                case RepeatRadialProfileOperation repeat:
                    result.Append($"{repeat.Count}:{R(repeat.ToothDepth)}:{R(repeat.ToothFraction)}:{R(repeat.RotationDegrees)}");
                    break;
                case ReplaceSegmentProfileOperation replace:
                    result.Append($"{replace.SegmentIndex}:{replace.Replacement.Kind}:{R(replace.Replacement.Amount)}:{Point(replace.Replacement.Control1)}:{Point(replace.Replacement.Control2)}");
                    break;
                case TransformProfileOperation transform:
                    result.Append($"{R(transform.A)}:{R(transform.B)}");
                    break;
            }
            result.Append('|');
        }
        return result.Append("yield|").Append(definition.YieldState).ToString();
    }

    private static string Shape(ProfileShapeSpec shape)
    {
        return shape switch
        {
            RectangleProfileShape value => $"Rectangle:{R(value.Width)}:{R(value.Height)}",
            RoundedRectangleProfileShape value => $"RoundedRectangle:{R(value.Width)}:{R(value.Height)}:{R(value.Radius)}",
            CircleProfileShape value => $"Circle:{R(value.Radius)}:{R(value.CenterX)}:{R(value.CenterY)}",
            EllipseProfileShape value => $"Ellipse:{R(value.RadiusX)}:{R(value.RadiusY)}:{R(value.CenterX)}:{R(value.CenterY)}",
            RegularPolygonProfileShape value => $"RegularPolygon:{value.Sides}:{R(value.Radius)}:{R(value.RotationDegrees)}",
            PolygonProfileShape value => "Polygon:" + string.Join(';', value.Points.Select(point => $"{R(point.X)},{R(point.Y)}")),
            SlotProfileShape value => $"Slot:{R(value.Length)}:{R(value.Width)}:{R(value.AngleDegrees)}:{R(value.CenterX)}:{R(value.CenterY)}",
            CapsuleProfileShape value => $"Capsule:{Point(value.From)}:{Point(value.To)}:{R(value.Width)}",
            _ => throw new InvalidOperationException(),
        };
    }

    private static ProfileCompilationResult Failure(
        ProfileDefinition definition,
        IReadOnlyList<ProfileDiagnostic> diagnostics,
        IReadOnlyList<ProfileStateSummary>? summaries = null)
    {
        return new ProfileCompilationResult(definition, null, summaries ?? [], diagnostics, null, null, null);
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;

    private static bool Finite(VectorPoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static string Point(VectorPoint point) => $"{R(point.X)},{R(point.Y)}";

    private static string SegmentHash(VectorSegment segment)
    {
        string identity = segment switch
        {
            VectorLine line => $"L:{Point(line.P0)}:{Point(line.P1)}",
            VectorQuadratic quadratic => $"Q:{Point(quadratic.P0)}:{Point(quadratic.P1)}:{Point(quadratic.P2)}",
            VectorCubic cubic => $"C:{Point(cubic.P0)}:{Point(cubic.P1)}:{Point(cubic.P2)}:{Point(cubic.P3)}",
            _ => throw new InvalidOperationException(),
        };
        return ProfileHash.Utf8(identity);
    }

    private static int SegmentCount(VectorShape shape) => shape.Contours.Sum(contour => contour.Segments.Count);

    private static bool Contains(VectorBounds outer, VectorBounds inner)
    {
        return inner.MinX > outer.MinX && inner.MaxX < outer.MaxX
            && inner.MinY > outer.MinY && inner.MaxY < outer.MaxY;
    }

    private static string R(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private sealed record WorkingState(
        VectorShape Shape,
        ProfileShapeSpec? Base,
        IReadOnlyList<TabProfileOperation> Tabs,
        IReadOnlyList<NotchProfileOperation> Notches,
        IReadOnlyList<VectorContour> Holes,
        IReadOnlyList<string> AppliedFeatureIds,
        IReadOnlyList<string> SegmentProvenance);
}
