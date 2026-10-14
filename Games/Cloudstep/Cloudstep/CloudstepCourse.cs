using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Spatial3D;

namespace Cloudstep;

public enum SurfaceKind
{
    Grass,
    Stone,
    Wood,
    Cloud,
}

/// <summary>An axis-aligned or rotated solid block of static scenery. Its top face is the walkable surface.</summary>
public sealed record CourseBlock(string Id, Vector3 Center, Vector3 HalfSize, SurfaceKind Kind, float Roll = 0, bool Solid = true)
{
    public Matrix4x4 Transform => Matrix4x4.CreateRotationZ(Roll) * Matrix4x4.CreateTranslation(Center);

    /// <summary>An axis-aligned block spanning the given corners.</summary>
    public static CourseBlock Span(string id, Vector3 min, Vector3 max, SurfaceKind kind) =>
        new(id, (min + max) / 2, (max - min) / 2, kind);
}

public enum PlatformMotion
{
    /// <summary>Ping-pongs between <see cref="CoursePlatform.From"/> and <see cref="CoursePlatform.To"/>, easing at the ends.</summary>
    Shuttle,

    /// <summary>Rotates about +Y through <see cref="CoursePlatform.From"/> at <see cref="CoursePlatform.AngularSpeed"/>.</summary>
    Turntable,

    /// <summary>Holds still until stood on, shakes, falls away, then returns.</summary>
    Crumble,
}

public sealed record CoursePlatform(string Id, PlatformMotion Motion, Vector3 From, Vector3 HalfSize, SurfaceKind Kind)
{
    public Vector3 To { get; init; }
    public double Period { get; init; } = 6;

    /// <summary>Fraction of each end of a shuttle period spent waiting.</summary>
    public double Dwell { get; init; } = 0.15;
    public float AngularSpeed { get; init; }
}

public readonly record struct CourseCheckpoint(Vector3 Position, float Yaw);

/// <summary>A kill volume: touching it costs a life.</summary>
public sealed record CourseHazard(string Id, Vector3 Min, Vector3 Max);

/// <summary>Everything authored about the course. Pure data: rules live in <see cref="CloudstepWorld"/>.</summary>
public sealed record CloudstepCourse(
    ImmutableArray<CourseBlock> Blocks,
    ImmutableArray<CoursePlatform> Platforms,
    ImmutableArray<Vector3> Gems,
    ImmutableArray<CourseCheckpoint> Checkpoints,
    ImmutableArray<CourseHazard> Hazards,
    Vector3 Goal,
    float KillHeight)
{
    public string Identity => "cloudstep.course.v1";

    public SpatialWorld3D BuildStatics()
    {
        var colliders = new List<Collider3D>();
        foreach (CourseBlock block in Blocks.Where(block => block.Solid))
        {
            colliders.Add(new Collider3D(block.Id, CollisionMesh3D.Box(block.HalfSize), block.Transform, SemanticOwnerId: block.Id));
        }
        return new SpatialWorld3D(colliders);
    }

    public SpatialWorld3D BuildHazards()
    {
        return new SpatialWorld3D(Hazards.Select(hazard => new Collider3D(hazard.Id,
            CollisionMesh3D.Box((hazard.Max - hazard.Min) / 2), Matrix4x4.CreateTranslation((hazard.Min + hazard.Max) / 2),
            SemanticOwnerId: hazard.Id)));
    }

    /// <summary>The floating-islands course: stairs, a gap, a ramp to a bonus ledge, a shuttle, a lift,
    /// a crumbling bridge, a turntable and a hazard strip before the goal beacon.</summary>
    public static CloudstepCourse Default()
    {
        var blocks = new List<CourseBlock>();
        void Island(string id, float x0, float x1, float z0, float z1, float top, float depth = 4)
        {
            blocks.Add(CourseBlock.Span(id, new(x0, top - depth, z0), new(x1, top, z1), SurfaceKind.Grass));
        }

        Island("start", -5, 5, -5, 5, 0);
        for (int step = 0; step < 6; step++)
        {
            float top = 0.25f * (step + 1);
            blocks.Add(CourseBlock.Span($"stair-{step}", new(-2, top - 3, -5.6f - 0.6f * step), new(2, top, -5f - 0.6f * step),
                SurfaceKind.Stone));
        }
        Island("isle-a", -4, 4, -15, -8.6f, 1.5f);
        Island("isle-b", -4, 4, -24, -18.5f, 1.5f);

        // A 22.6 degree ramp from isle B up to a bonus ledge.
        float roll = MathF.Atan2(2.5f, 6);
        var rampHalf = new Vector3(3.3f, 0.2f, 1.5f);
        var rampTop = new Vector3(7, 2.75f, -22);
        blocks.Add(new CourseBlock("ramp", rampTop + new Vector3(MathF.Sin(roll) * rampHalf.Y, -MathF.Cos(roll) * rampHalf.Y, 0),
            rampHalf, SurfaceKind.Wood, roll));
        blocks.Add(CourseBlock.Span("bonus-ledge", new(10, 1, -24), new(13, 4, -20), SurfaceKind.Stone));

        Island("isle-c", -4, 4, -44, -36.5f, 1.5f);
        blocks.Add(CourseBlock.Span("isle-d", new(9.5f, 3, -46), new(16, 7, -36), SurfaceKind.Grass));
        Island("isle-e", 8, 17, -71, -62, 7);
        blocks.Add(CourseBlock.Span("turntable-pillar", new(12, -6, -78.5f), new(13, 6.5f, -77.5f), SurfaceKind.Stone));
        Island("isle-f", 8, 17, -96, -84.5f, 7);

        // Visual only: a sea of cloud far below.
        blocks.Add(new CourseBlock("cloud-sea", new(0, -24, -45), new(160, 0.5f, 160), SurfaceKind.Cloud, Solid: false));

        var platforms = ImmutableArray.Create(
            new CoursePlatform("shuttle", PlatformMotion.Shuttle, new(0, 1.3f, -26.5f), new(1.5f, 0.2f, 1.5f), SurfaceKind.Wood)
            {
                To = new(0, 1.3f, -34.5f),
                Period = 7,
            },
            new CoursePlatform("lift", PlatformMotion.Shuttle, new(6.5f, 1.3f, -41), new(1.5f, 0.2f, 1.5f), SurfaceKind.Wood)
            {
                To = new(6.5f, 6.8f, -41),
                Period = 6,
                Dwell = 0.2,
            },
            new CoursePlatform("crumble-0", PlatformMotion.Crumble, new(12.75f, 6.8f, -48.5f), new(1, 0.2f, 1), SurfaceKind.Stone),
            new CoursePlatform("crumble-1", PlatformMotion.Crumble, new(12.75f, 6.8f, -51.5f), new(1, 0.2f, 1), SurfaceKind.Stone),
            new CoursePlatform("crumble-2", PlatformMotion.Crumble, new(12.75f, 6.8f, -54.5f), new(1, 0.2f, 1), SurfaceKind.Stone),
            new CoursePlatform("crumble-3", PlatformMotion.Crumble, new(12.75f, 6.8f, -57.5f), new(1, 0.2f, 1), SurfaceKind.Stone),
            new CoursePlatform("crumble-4", PlatformMotion.Crumble, new(12.75f, 6.8f, -60.5f), new(1, 0.2f, 1), SurfaceKind.Stone),
            new CoursePlatform("turntable", PlatformMotion.Turntable, new(12.5f, 6.8f, -78), new(1.3f, 0.2f, 5.2f), SurfaceKind.Wood)
            {
                AngularSpeed = 0.3f,
            });

        ImmutableArray<Vector3> gems =
        [
            new(-2, 1, 0), new(0, 1, -2), new(2, 1, 0),
            new(0, 2.6f, -7.6f),
            new(0, 2.6f, -12),
            new(0, 3.4f, -16.8f),
            new(-2.5f, 2.6f, -21),
            new(7, 3.9f, -22), new(11.3f, 5, -22.6f), new(12.1f, 5, -21.2f),
            new(0, 3.1f, -30.5f),
            new(-3, 2.6f, -40),
            new(6.5f, 5, -41),
            new(13, 8.1f, -41), new(15, 8.1f, -38),
            new(12.75f, 8.1f, -51.5f), new(12.75f, 8.1f, -57.5f),
            new(10, 8.1f, -66), new(15, 8.1f, -68),
            new(12.5f, 8.2f, -78),
            new(12.5f, 8.6f, -87.5f),
        ];

        ImmutableArray<CourseCheckpoint> checkpoints =
        [
            new(new(0, 0, 3), 0),
            new(new(0, 1.5f, -39), 0),
            new(new(12.5f, 7, -64.5f), 0),
        ];

        ImmutableArray<CourseHazard> hazards =
        [
            new("thorns", new(8, 7, -88.2f), new(17, 7.5f, -86.8f)),
        ];

        return new CloudstepCourse([.. blocks], platforms, gems, checkpoints, hazards, new(12.5f, 7, -92), -12);
    }
}
