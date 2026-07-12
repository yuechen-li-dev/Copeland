namespace Machina.Fonts.ReferenceRendering;

public static class InkMaskDiff
{
    public static ShapeDiffMetrics Compare(
        InkMask left,
        InkMask right,
        double baselineY,
        float threshold = 0.001f)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        ValidateSameSize(left, right);

        InkMaskBounds? leftBounds = left.ComputeBounds(threshold);
        InkMaskBounds? rightBounds = right.ComputeBounds(threshold);

        int leftInkArea = 0;
        int rightInkArea = 0;
        int intersectionArea = 0;
        int leftOnlyArea = 0;
        int rightOnlyArea = 0;
        int aboveBaselineExtraArea = 0;
        int belowBaselineExtraArea = 0;

        for (int y = 0; y < left.Height; y++)
        {
            for (int x = 0; x < left.Width; x++)
            {
                bool leftInk = left.IsInk(x, y, threshold);
                bool rightInk = right.IsInk(x, y, threshold);

                if (leftInk)
                {
                    leftInkArea++;
                }

                if (rightInk)
                {
                    rightInkArea++;
                }

                if (leftInk && rightInk)
                {
                    intersectionArea++;
                    continue;
                }

                if (leftInk)
                {
                    leftOnlyArea++;
                }
                else if (rightInk)
                {
                    rightOnlyArea++;
                }

                if (leftInk || rightInk)
                {
                    if ((y + 0.5d) >= baselineY)
                    {
                        belowBaselineExtraArea++;
                    }
                    else
                    {
                        aboveBaselineExtraArea++;
                    }
                }
            }
        }

        int unionArea = intersectionArea + leftOnlyArea + rightOnlyArea;
        double intersectionOverUnion = unionArea == 0 ? 1d : intersectionArea / (double)unionArea;
        EdgeDistanceSummary edgeDistance = ComputeEdgeDistances(left, right, threshold);

        return new ShapeDiffMetrics(
            leftBounds,
            rightBounds,
            GetDelta(leftBounds?.Left, rightBounds?.Left),
            GetDelta(leftBounds?.Top, rightBounds?.Top),
            GetDelta(leftBounds?.Right, rightBounds?.Right),
            GetDelta(leftBounds?.Bottom, rightBounds?.Bottom),
            GetDelta(leftBounds?.Width, rightBounds?.Width),
            GetDelta(leftBounds?.Height, rightBounds?.Height),
            leftInkArea,
            rightInkArea,
            intersectionArea,
            unionArea,
            leftOnlyArea,
            rightOnlyArea,
            intersectionOverUnion,
            edgeDistance.Mean,
            edgeDistance.P50,
            edgeDistance.P95,
            edgeDistance.Max,
            aboveBaselineExtraArea,
            belowBaselineExtraArea,
            leftOnlyArea + rightOnlyArea);
    }

    private static EdgeDistanceSummary ComputeEdgeDistances(
        InkMask left,
        InkMask right,
        float threshold)
    {
        IReadOnlyList<InkMaskPoint> leftEdges = left.ExtractEdges(threshold);
        IReadOnlyList<InkMaskPoint> rightEdges = right.ExtractEdges(threshold);
        double fallbackDistance = Math.Sqrt((left.Width * left.Width) + (left.Height * left.Height));

        if (leftEdges.Count == 0 && rightEdges.Count == 0)
        {
            return new EdgeDistanceSummary(0d, 0d, 0d, 0d);
        }

        List<double> distances = [];
        AppendDirectedDistances(leftEdges, rightEdges, fallbackDistance, distances);
        AppendDirectedDistances(rightEdges, leftEdges, fallbackDistance, distances);

        if (distances.Count == 0)
        {
            return new EdgeDistanceSummary(0d, 0d, 0d, 0d);
        }

        distances.Sort();

        return new EdgeDistanceSummary(
            distances.Average(),
            Percentile(distances, 0.50d),
            Percentile(distances, 0.95d),
            distances[^1]);
    }

    private static void AppendDirectedDistances(
        IReadOnlyList<InkMaskPoint> source,
        IReadOnlyList<InkMaskPoint> target,
        double fallbackDistance,
        List<double> destination)
    {
        if (source.Count == 0)
        {
            return;
        }

        if (target.Count == 0)
        {
            destination.AddRange(Enumerable.Repeat(fallbackDistance, source.Count));
            return;
        }

        foreach (InkMaskPoint sourcePoint in source)
        {
            double best = double.MaxValue;

            foreach (InkMaskPoint targetPoint in target)
            {
                int deltaX = sourcePoint.X - targetPoint.X;
                int deltaY = sourcePoint.Y - targetPoint.Y;
                double distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                if (distance < best)
                {
                    best = distance;
                }
            }

            destination.Add(best);
        }
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0d;
        }

        double position = (values.Count - 1) * percentile;
        int lowerIndex = (int)Math.Floor(position);
        int upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return values[lowerIndex];
        }

        double fraction = position - lowerIndex;
        return values[lowerIndex] + ((values[upperIndex] - values[lowerIndex]) * fraction);
    }

    private static int? GetDelta(int? left, int? right)
    {
        return left.HasValue && right.HasValue
            ? right.Value - left.Value
            : null;
    }

    private static void ValidateSameSize(InkMask left, InkMask right)
    {
        if (left.Width != right.Width || left.Height != right.Height)
        {
            throw new InvalidOperationException(
                $"Ink masks must have the same size. Left={left.Width}x{left.Height}, right={right.Width}x{right.Height}.");
        }
    }

    private sealed record EdgeDistanceSummary(
        double Mean,
        double P50,
        double P95,
        double Max);
}
