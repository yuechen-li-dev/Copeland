namespace Aurelian.Field2D;

public enum FieldDisturbanceShape
{
    Point,
    Line,
    Arc,
    Ring
}

public sealed record FieldDisturbance(
    FieldDisturbanceShape Shape,
    double X,
    double Y,
    double Strength,
    double Radius,
    double Width = 1,
    double Length = 0,
    double AngleRadians = 0,
    double ArcRadians = Math.PI,
    string? SemanticPayload = null);

public sealed record ReactiveFluidSnapshot(
    int Width,
    int Height,
    double CellSize,
    float[] HeightField,
    float[] Velocity,
    float[] Foam,
    float[] Charge,
    float[] FlowX,
    float[] FlowY,
    byte[] LiquidMask,
    byte[] ConductiveMask,
    long Tick);

public sealed class ReactiveFluid2D
{
    private const double FixedStepSeconds = 1.0 / 60.0;
    private const int MaximumCatchUpSteps = 6;
    private readonly float[] nextHeight;
    private readonly float[] nextVelocity;
    private readonly float[] nextCharge;
    private readonly int[] queue;
    private readonly uint[] visits;
    private uint visitGeneration;
    private double accumulator;

    public ReactiveFluid2D(int width, int height, double cellSize)
    {
        HeightField = new Field2D<float>(width, height, cellSize);
        Velocity = new Field2D<float>(width, height, cellSize);
        Foam = new Field2D<float>(width, height, cellSize);
        Charge = new Field2D<float>(width, height, cellSize);
        FlowX = new Field2D<float>(width, height, cellSize);
        FlowY = new Field2D<float>(width, height, cellSize);
        LiquidMask = new Field2D<byte>(width, height, cellSize);
        ConductiveMask = new Field2D<byte>(width, height, cellSize);
        nextHeight = new float[HeightField.Count];
        nextVelocity = new float[HeightField.Count];
        nextCharge = new float[HeightField.Count];
        queue = new int[HeightField.Count];
        visits = new uint[HeightField.Count];
    }

    public Field2D<float> HeightField { get; }
    public Field2D<float> Velocity { get; }
    public Field2D<float> Foam { get; }
    public Field2D<float> Charge { get; }
    public Field2D<float> FlowX { get; }
    public Field2D<float> FlowY { get; }
    public Field2D<byte> LiquidMask { get; }
    public Field2D<byte> ConductiveMask { get; }
    public long Tick { get; private set; }

    public int AdvanceFrame(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }
        accumulator = Math.Min(accumulator + elapsedSeconds, FixedStepSeconds * MaximumCatchUpSteps);
        int steps = 0;
        while (accumulator + 1e-12 >= FixedStepSeconds && steps < MaximumCatchUpSteps)
        {
            Step();
            accumulator -= FixedStepSeconds;
            steps++;
        }
        return steps;
    }

    public int ApplyDisturbance(FieldDisturbance disturbance)
    {
        Validate(disturbance);
        (int seedX, int seedY) = LiquidMask.CellAt(disturbance.X, disturbance.Y);
        int seed = LiquidMask.Index(seedX, seedY);
        if (LiquidMask.ReadOnlyCells[seed] == 0)
        {
            return 0;
        }

        uint generation = NextGeneration();
        int read = 0;
        int write = 1;
        int affected = 0;
        queue[0] = seed;
        visits[seed] = generation;
        double reach = Math.Max(disturbance.Radius, disturbance.Length) + disturbance.Width;
        while (read < write)
        {
            int index = queue[read++];
            int x = index % HeightField.Width;
            int y = index / HeightField.Width;
            double localX = ((x + 0.5) * HeightField.CellSize) - disturbance.X;
            double localY = ((y + 0.5) * HeightField.CellSize) - disturbance.Y;
            double influence = Influence(disturbance, localX, localY);
            if (influence > 0)
            {
                float impulse = (float)(disturbance.Strength * influence);
                HeightField.Cells[index] = Math.Clamp(HeightField.Cells[index] + (impulse * 0.33f), -1.5f, 1.5f);
                Velocity.Cells[index] = Math.Clamp(Velocity.Cells[index] + (impulse * 0.13f), -0.85f, 0.85f);
                Foam.Cells[index] = Math.Clamp(Foam.Cells[index] + (Math.Abs(impulse) * 0.38f), 0, 1);
                affected++;
            }

            if ((localX * localX) + (localY * localY) <= reach * reach)
            {
                EnqueueConnected(x - 1, y, generation, ref write);
                EnqueueConnected(x + 1, y, generation, ref write);
                EnqueueConnected(x, y - 1, generation, ref write);
                EnqueueConnected(x, y + 1, generation, ref write);
            }
        }
        return affected;
    }

    public int Energize(double worldX, double worldY, double strength, double radius)
    {
        if (!double.IsFinite(strength) || !double.IsFinite(radius) || strength < 0 || radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(strength));
        }
        (int seedX, int seedY) = ConductiveMask.CellAt(worldX, worldY);
        int seed = ConductiveMask.Index(seedX, seedY);
        if (LiquidMask.ReadOnlyCells[seed] == 0 || ConductiveMask.ReadOnlyCells[seed] == 0)
        {
            return 0;
        }

        uint generation = NextGeneration();
        int read = 0;
        int write = 1;
        int affected = 0;
        queue[0] = seed;
        visits[seed] = generation;
        while (read < write)
        {
            int index = queue[read++];
            int x = index % Charge.Width;
            int y = index / Charge.Width;
            double dx = ((x + 0.5) * Charge.CellSize) - worldX;
            double dy = ((y + 0.5) * Charge.CellSize) - worldY;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance <= radius)
            {
                float value = (float)(strength * (1 - (distance / Math.Max(radius, Charge.CellSize))));
                Charge.Cells[index] = Math.Clamp(Math.Max(Charge.Cells[index], value), 0, 1);
                affected++;
                EnqueueConductive(x - 1, y, generation, ref write);
                EnqueueConductive(x + 1, y, generation, ref write);
                EnqueueConductive(x, y - 1, generation, ref write);
                EnqueueConductive(x, y + 1, generation, ref write);
            }
        }
        return affected;
    }

    public (float Height, float Velocity, float Foam, float Charge, float FlowX, float FlowY, bool Liquid) Sample(
        double worldX,
        double worldY)
    {
        return (
            HeightField.SampleNearest(worldX, worldY),
            Velocity.SampleNearest(worldX, worldY),
            Foam.SampleNearest(worldX, worldY),
            Charge.SampleNearest(worldX, worldY),
            FlowX.SampleNearest(worldX, worldY),
            FlowY.SampleNearest(worldX, worldY),
            LiquidMask.SampleNearest(worldX, worldY) != 0);
    }

    public byte[] ProjectRgba8()
    {
        var pixels = new byte[HeightField.Count * 4];
        for (int index = 0; index < HeightField.Count; index++)
        {
            pixels[(index * 4) + 0] = ToByte((HeightField.ReadOnlyCells[index] + 1.5f) / 3f);
            pixels[(index * 4) + 1] = ToByte(Foam.ReadOnlyCells[index]);
            pixels[(index * 4) + 2] = LiquidMask.ReadOnlyCells[index] == 0 ? (byte)0 : (byte)255;
            pixels[(index * 4) + 3] = ToByte(Charge.ReadOnlyCells[index]);
        }
        return pixels;
    }

    public ReactiveFluidSnapshot Snapshot()
    {
        return new ReactiveFluidSnapshot(
            HeightField.Width,
            HeightField.Height,
            HeightField.CellSize,
            HeightField.Snapshot(),
            Velocity.Snapshot(),
            Foam.Snapshot(),
            Charge.Snapshot(),
            FlowX.Snapshot(),
            FlowY.Snapshot(),
            LiquidMask.Snapshot(),
            ConductiveMask.Snapshot(),
            Tick);
    }

    private void Step()
    {
        int width = HeightField.Width;
        int height = HeightField.Height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                if (LiquidMask.ReadOnlyCells[index] == 0)
                {
                    nextHeight[index] = 0;
                    nextVelocity[index] = 0;
                    nextCharge[index] = 0;
                    continue;
                }

                float center = HeightField.ReadOnlyCells[index];
                float neighbors = NeighborHeight(x - 1, y, center)
                    + NeighborHeight(x + 1, y, center)
                    + NeighborHeight(x, y - 1, center)
                    + NeighborHeight(x, y + 1, center);
                float velocity = (Velocity.ReadOnlyCells[index] + ((neighbors - (center * 4)) * 0.16f)) * 0.985f;
                nextVelocity[index] = velocity;
                nextHeight[index] = (center + velocity) * 0.997f;

                float chargeNeighbors = NeighborCharge(x - 1, y)
                    + NeighborCharge(x + 1, y)
                    + NeighborCharge(x, y - 1)
                    + NeighborCharge(x, y + 1);
                nextCharge[index] = Math.Clamp(
                    (Charge.ReadOnlyCells[index] * 0.94f) + (chargeNeighbors * 0.0125f),
                    0,
                    1);
                Foam.Cells[index] *= 0.965f;
            }
        }
        nextHeight.CopyTo(HeightField.Cells);
        nextVelocity.CopyTo(Velocity.Cells);
        nextCharge.CopyTo(Charge.Cells);
        Tick++;
    }

    private float NeighborHeight(int x, int y, float dryFallback)
    {
        if ((uint)x >= HeightField.Width || (uint)y >= HeightField.Height)
        {
            return dryFallback;
        }
        int index = (y * HeightField.Width) + x;
        return LiquidMask.ReadOnlyCells[index] == 0 ? dryFallback : HeightField.ReadOnlyCells[index];
    }

    private float NeighborCharge(int x, int y)
    {
        if ((uint)x >= Charge.Width || (uint)y >= Charge.Height)
        {
            return 0;
        }
        int index = (y * Charge.Width) + x;
        return LiquidMask.ReadOnlyCells[index] == 0 || ConductiveMask.ReadOnlyCells[index] == 0
            ? 0
            : Charge.ReadOnlyCells[index];
    }

    private void EnqueueConnected(int x, int y, uint generation, ref int write)
    {
        if ((uint)x >= LiquidMask.Width || (uint)y >= LiquidMask.Height)
        {
            return;
        }
        int index = (y * LiquidMask.Width) + x;
        if (LiquidMask.ReadOnlyCells[index] == 0 || visits[index] == generation)
        {
            return;
        }
        visits[index] = generation;
        queue[write++] = index;
    }

    private void EnqueueConductive(int x, int y, uint generation, ref int write)
    {
        if ((uint)x >= ConductiveMask.Width || (uint)y >= ConductiveMask.Height)
        {
            return;
        }
        int index = (y * ConductiveMask.Width) + x;
        if (LiquidMask.ReadOnlyCells[index] == 0
            || ConductiveMask.ReadOnlyCells[index] == 0
            || visits[index] == generation)
        {
            return;
        }
        visits[index] = generation;
        queue[write++] = index;
    }

    private uint NextGeneration()
    {
        visitGeneration++;
        if (visitGeneration == 0)
        {
            Array.Clear(visits);
            visitGeneration = 1;
        }
        return visitGeneration;
    }

    private static double Influence(FieldDisturbance disturbance, double x, double y)
    {
        double distance = Math.Sqrt((x * x) + (y * y));
        return disturbance.Shape switch
        {
            FieldDisturbanceShape.Point => Falloff(distance, disturbance.Radius),
            FieldDisturbanceShape.Ring => Falloff(Math.Abs(distance - disturbance.Radius), disturbance.Width / 2),
            FieldDisturbanceShape.Line => LineInfluence(disturbance, x, y),
            FieldDisturbanceShape.Arc => ArcInfluence(disturbance, x, y, distance),
            _ => 0
        };
    }

    private static double LineInfluence(FieldDisturbance disturbance, double x, double y)
    {
        double forward = (x * Math.Cos(disturbance.AngleRadians)) + (y * Math.Sin(disturbance.AngleRadians));
        double lateral = Math.Abs((-x * Math.Sin(disturbance.AngleRadians)) + (y * Math.Cos(disturbance.AngleRadians)));
        if (forward < 0 || forward > disturbance.Length)
        {
            return 0;
        }
        return Falloff(lateral, disturbance.Width / 2);
    }

    private static double ArcInfluence(FieldDisturbance disturbance, double x, double y, double distance)
    {
        if (distance > disturbance.Radius)
        {
            return 0;
        }
        double angle = Math.Atan2(y, x) - disturbance.AngleRadians;
        angle = Math.Atan2(Math.Sin(angle), Math.Cos(angle));
        if (Math.Abs(angle) > disturbance.ArcRadians / 2)
        {
            return 0;
        }
        return Falloff(distance, disturbance.Radius);
    }

    private static double Falloff(double distance, double radius)
    {
        if (radius <= 0)
        {
            return distance <= 1e-9 ? 1 : 0;
        }
        double normalized = Math.Clamp(1 - (distance / radius), 0, 1);
        return normalized * normalized;
    }

    private static void Validate(FieldDisturbance disturbance)
    {
        if (!double.IsFinite(disturbance.X)
            || !double.IsFinite(disturbance.Y)
            || !double.IsFinite(disturbance.Strength)
            || !double.IsFinite(disturbance.Radius)
            || !double.IsFinite(disturbance.Width)
            || !double.IsFinite(disturbance.Length)
            || !double.IsFinite(disturbance.AngleRadians)
            || !double.IsFinite(disturbance.ArcRadians)
            || disturbance.Radius < 0
            || disturbance.Width < 0
            || disturbance.Length < 0
            || disturbance.ArcRadians <= 0
            || disturbance.ArcRadians > Math.PI * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(disturbance));
        }
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
