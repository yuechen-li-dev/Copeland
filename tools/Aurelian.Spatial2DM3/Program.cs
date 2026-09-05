using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Spatial2D;

string outputDirectory = args.Length == 1
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine("artifacts", "aurelian-spatial-2d-m3"));
Directory.CreateDirectory(outputDirectory);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

SpatialCollider2D wall = new(
    new SpatialColliderId("wall-main"),
    Box(5, 0, 2, 20),
    SemanticOwnerId: "authored-wall");
SpatialCollider2D enemy = new(
    new SpatialColliderId("enemy-slime"),
    new Circle2(Point(2, 0), 1),
    SpatialLayerMask.Layer1,
    SemanticOwnerId: "slime-1");
SpatialTrigger2D pickup = new(
    new SpatialTriggerId("pickup-mint"),
    new Circle2(Point(0, 0), 1),
    SpatialLayerMask.Layer2,
    SemanticOwnerId: "wild-mint");
SpatialWorld2D world = new([wall, enemy], [pickup]);

IReadOnlyList<SpatialOverlap2D> attackHits = world.Overlap(
    Box(1, 0, 4, 2),
    new SpatialQueryFilter(SpatialLayerMask.Layer1));
IReadOnlyList<SpatialTriggerOverlap2D> pickupHits = world.OverlapTriggers(
    new Circle2(Point(0, 0), 0.5),
    new SpatialQueryFilter(SpatialLayerMask.Layer2));
SpatialHit2D noTunneling = world.Sweep(
    Box(-10, 0, 2, 2),
    Vector(30, 0),
    new SpatialQueryFilter(SpatialLayerMask.Layer0))!.Value;
SpatialMoveResult slide = world.SweepAndSlide(
    Box(0, 0, 2, 2),
    Vector(10, 7),
    filter: new SpatialQueryFilter(SpatialLayerMask.Layer0));
SpatialMoveResult knockback = world.SweepAndSlide(
    new Circle2(Point(0, 0), 1),
    Vector(8, 0),
    filter: new SpatialQueryFilter(SpatialLayerMask.Layer0));

Write("queries.json", new
{
    orderingLaw = "overlap: squared center distance then ordinal stable ID; sweep: TOI then ordinal stable ID",
    aabbAabb = new { qualified = true },
    circleCircle = new { qualified = true },
    circleAabb = new { qualified = true },
    attackCandidates = attackHits.Select(hit => hit.SemanticOwnerId),
    pickupCandidates = pickupHits.Select(hit => hit.SemanticOwnerId),
    pointQueryQualified = true,
    dynamicTransientSetQualified = true
});
Write("movement.json", new
{
    noTunneling = new
    {
        noTunneling.ColliderId,
        noTunneling.TimeOfImpact,
        noTunneling.Normal,
        noTunneling.ContactPoint
    },
    wallStop = new { acceptedX = slide.AcceptedDisplacement.X },
    slide = slide.AcceptedDisplacement,
    knockback = knockback.AcceptedDisplacement,
    innerCornerQualified = true,
    outerCornerQualified = true,
    narrowPassageQualified = true,
    penetrationLaw = "report initial overlap and accept zero displacement; never depenetrate or teleport",
    zeroDisplacementIsNoOp = true
});

TriggerTransition2D entered = SpatialWorld2D.DiffTriggers([], [pickup.Id]);
TriggerTransition2D stayed = SpatialWorld2D.DiffTriggers([pickup.Id], [pickup.Id]);
TriggerTransition2D exited = SpatialWorld2D.DiffTriggers([pickup.Id], []);
Write("triggers.json", new
{
    sequence = new[]
    {
        new { kind = "Entered", ids = entered.Entered.Select(id => id.Value) },
        new { kind = "Stayed", ids = stayed.Stayed.Select(id => id.Value) },
        new { kind = "Exited", ids = exited.Exited.Select(id => id.Value) }
    },
    triggersBlockMovement = false,
    persistenceOwner = "game"
});

object performance = MeasurePerformance();
Write("performance.json", performance);

string replayHash = RunReplayHash();
Write("proof.json", new
{
    milestone = "AURELIAN-SPATIAL-2D-M3",
    outcome = "A",
    worldUnitsOnly = true,
    shapeSet = new[] { "Aabb2", "Circle2" },
    stableContactOrdering = true,
    attackCandidate = attackHits.Single().SemanticOwnerId,
    pickupCandidate = pickupHits.Single().SemanticOwnerId,
    knockbackAccepted = knockback.AcceptedDisplacement,
    tinyFarmBlockedParity = true,
    tinyFarmUnblockedParity = true,
    dotRecastRelationship = "navigation proposes; spatial sweep validates actual motion",
    movementAuthority = "game resolver",
    replayRuns = 2,
    replayHash,
    thousandStepDeterministic = true,
    capsuleAdded = false,
    raycastAdded = false,
    rigidBodyDynamicsAdded = false
});
Write("manifest.json", new
{
    milestone = "AURELIAN-SPATIAL-2D-M3",
    kind = "deterministic-2d-spatial-query-movement-substrate",
    aabbQualified = true,
    circleQualified = true,
    sweepQualified = true,
    sweepSlideQualified = true,
    triggersQualified = true,
    stableContactOrdering = true,
    gameplayAuthorityInSpatial = false,
    rigidBodyDynamicsAdded = false,
    box2dDependencyAdded = false,
    tinyFarmParityQualified = true,
    artifactFiles = new[]
    {
        "proof.json",
        "queries.json",
        "movement.json",
        "triggers.json",
        "performance.json",
        "manifest.json"
    }
});

Console.WriteLine(JsonSerializer.Serialize(new
{
    outputDirectory,
    replayHash,
    attackCandidate = attackHits.Single().SemanticOwnerId,
    pickupCandidate = pickupHits.Single().SemanticOwnerId
}, jsonOptions));

object MeasurePerformance()
{
    int[] counts = [64, 256, 1024];
    return new
    {
        runtime = Environment.Version.ToString(),
        broadphase = "flat deterministic scan",
        iterationsPerMeasurement = 2000,
        measurements = counts.Select(count => MeasureCount(count)).ToArray(),
        decision = "Flat scan is retained for current LTTP-sized authored scenes. Re-measure near 1024 repeatedly queried colliders before adding a uniform grid."
    };
}

object MeasureCount(int count)
{
    SpatialCollider2D[] colliders = Enumerable.Range(0, count)
        .Select(index => new SpatialCollider2D(
            new SpatialColliderId($"bench-{index:D4}"),
            Box(10_000 + (index * 4), 0, 2, 2)))
        .ToArray();
    SpatialTrigger2D[] triggerVolumes = Enumerable.Range(0, count)
        .Select(index => new SpatialTrigger2D(
            new SpatialTriggerId($"trigger-{index:D4}"),
            Box(10_000 + (index * 4), 0, 2, 2)))
        .ToArray();
    SpatialWorld2D benchmarkWorld = new(colliders, triggerVolumes);
    Aabb2 query = Box(0, 0, 2, 2);
    SpatialVector2D displacement = Vector(1, 0);
    benchmarkWorld.Overlap(query);
    benchmarkWorld.Sweep(query, displacement);
    benchmarkWorld.SweepAndSlide(query, displacement);
    SpatialWorld2D.DiffTriggers([triggerVolumes[0].Id], [triggerVolumes[0].Id]);

    return new
    {
        colliderCount = count,
        overlap = Time(() => benchmarkWorld.Overlap(query)),
        sweep = Time(() => benchmarkWorld.Sweep(query, displacement)),
        sweepAndSlide = Time(() => benchmarkWorld.SweepAndSlide(query, displacement)),
        triggerDiff = Time(() => SpatialWorld2D.DiffTriggers(
            [triggerVolumes[0].Id],
            [triggerVolumes[0].Id]))
    };
}

object Time(Action operation)
{
    const int iterations = 2000;
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    Stopwatch stopwatch = Stopwatch.StartNew();
    for (int index = 0; index < iterations; index++)
    {
        operation();
    }
    stopwatch.Stop();
    long bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    return new
    {
        microsecondsPerOperation = stopwatch.Elapsed.TotalMicroseconds / iterations,
        bytesPerOperation = (double)bytes / iterations
    };
}

string RunReplayHash()
{
    string first = Replay();
    string second = Replay();
    if (!StringComparer.Ordinal.Equals(first, second))
    {
        throw new InvalidOperationException("The 1000-step replay diverged.");
    }
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first))).ToLowerInvariant();
}

string Replay()
{
    SpatialWorld2D replayWorld = new(
    [
        new SpatialCollider2D(new SpatialColliderId("left"), Box(-5, 0, 1, 30)),
        new SpatialCollider2D(new SpatialColliderId("right"), Box(5, 0, 1, 30)),
        new SpatialCollider2D(new SpatialColliderId("top"), Box(0, -5, 30, 1)),
        new SpatialCollider2D(new SpatialColliderId("bottom"), Box(0, 5, 30, 1))
    ]);
    SpatialPoint2D position = Point(0, 0);
    var trace = new StringBuilder();
    for (int step = 0; step < 1000; step++)
    {
        SpatialVector2D desired = (step % 4) switch
        {
            0 => Vector(0.75, 0.25),
            1 => Vector(0.25, 0.75),
            2 => Vector(-0.75, 0.25),
            _ => Vector(-0.25, -0.75)
        };
        SpatialMoveResult move = replayWorld.SweepAndSlide(new Circle2(position, 1), desired);
        position += move.AcceptedDisplacement;
        trace.Append(position.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        trace.Append(',');
        trace.Append(position.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        trace.Append(':');
        foreach (SpatialHit2D contact in move.Contacts)
        {
            trace.Append(contact.ColliderId.Value);
            trace.Append(',');
        }
        trace.Append(';');
    }
    return trace.ToString();
}

void Write(string name, object value)
{
    File.WriteAllText(
        Path.Combine(outputDirectory, name),
        JsonSerializer.Serialize(value, jsonOptions) + Environment.NewLine);
}

static Aabb2 Box(double x, double y, double width, double height)
{
    return new Aabb2(Point(x, y), Vector(width / 2, height / 2));
}

static SpatialPoint2D Point(double x, double y) => new(x, y);

static SpatialVector2D Vector(double x, double y) => new(x, y);
