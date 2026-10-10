using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Cloth3D.Vulkan;
using Aurelian.NativeComposition;
using Aurelian.Spatial3D;
using Aurelian.World.Scenes;
using Xunit;

namespace Aurelian.Cloth3D.Tests;

public sealed class ClothFoundationTests
{
    private static ClothDefinition3D Sheet(int side = 9) => ClothDefinition3D.Grid("test", side, side, new(2, 2), new(-1, 2, -1)) with { Pins = [0, side - 1] };

    [Fact]
    public void CompiledSchedulesNeverShareVerticesAndMassMatchesPatternArea()
    {
        var plan = Sheet().Compile();
        foreach (var color in plan.ConstraintColors)
        {
            var vertices = color.SelectMany(index => plan.Constraints[index].Vertices).ToArray();
            Assert.Equal(vertices.Length, vertices.Distinct().Count());
        }
        foreach (var color in plan.TriangleColors)
        {
            var vertices = color.SelectMany(index => plan.Definition.Indices.Skip(index * 3).Take(3)).ToArray();
            Assert.Equal(vertices.Length, vertices.Distinct().Count());
        }
        var unpinned = (Sheet() with { Pins = [] }).Compile();
        Assert.InRange(unpinned.InverseMasses.Sum(inverse => 1 / inverse), .79999f, .80001f);
        Assert.Contains(plan.Constraints, constraint => constraint.Kind == ClothConstraintKind3D.FlatBend);
    }

    [Fact]
    public void GravityDrapePinsAndSnapshotReplayUseTheRealSolver()
    {
        var plan = Sheet().Compile();
        using var solver = new ClothSolver3D(plan);
        var contacts = new ClothContacts3D { PlaneNormal = Vector3.UnitY, SphereCenter = new(0, .6f, 0), SphereRadius = .5f };
        var options = new ClothStepOptions3D();
        for (int step = 0; step < 120; step++)
        {
            solver.Step(options, contacts);
        }
        var snapshot = solver.Capture();
        var metrics = ClothState3D.Measure(plan, snapshot, contacts);
        Assert.True(metrics.Finite);
        Assert.True(snapshot.Positions.Min(position => position.Y) < 1.5f);
        Assert.InRange(metrics.MaximumPinError, 0, .000001f);
        Assert.InRange(metrics.MaximumSurfacePenetration, 0, .005f);
        Assert.InRange(metrics.MaximumStretch, 0, .1f);
        solver.Step(options, contacts);
        var next = solver.Capture();
        solver.Restore(snapshot);
        solver.Step(options, contacts);
        Assert.True(next.Positions.SequenceEqual(solver.Capture().Positions));
        Assert.True(next.Velocities.SequenceEqual(solver.Capture().Velocities));
    }

    [Fact]
    public void TriangleInteriorContactFixesAVertexOnlyMiss()
    {
        var definition = new ClothDefinition3D("face", [new(-2, .1f, -2), new(2, .1f, -2), new(0, .1f, 2)],
            [new(0, 0), new(4, 0), new(2, 4)], [0, 1, 2]);
        var plan = definition.Compile();
        using var solver = new ClothSolver3D(plan);
        var contacts = new ClothContacts3D { SphereCenter = Vector3.Zero, SphereRadius = .5f };
        Assert.All(definition.Positions, position => Assert.True(position.Length() > .5f));
        Assert.True(ClothState3D.Measure(plan, solver.Capture(), contacts).MaximumSurfacePenetration > .3f);
        solver.Step(new() { Gravity = Vector3.Zero, Substeps = 1, Iterations = 1, ContactIterations = 8 }, contacts);
        Assert.InRange(ClothState3D.Measure(plan, solver.Capture(), contacts).MaximumSurfacePenetration, 0, .0001f);
    }

    [Fact]
    public void DegenerateSurfaceQueryHasFiniteBarycentricCoordinates()
    {
        var result = TriangleSurface3D.ClosestPoint(new(.5f, 2, 0), Vector3.Zero, Vector3.UnitX, Vector3.UnitX * 2);
        Assert.InRange(Vector3.Distance(result.Point, new(.5f, 0, 0)), 0, .00001f);
        Assert.InRange(MathF.Abs(result.Barycentric.X + result.Barycentric.Y + result.Barycentric.Z - 1), 0, .00001f);
    }

    [Fact]
    public void BendingIsZeroUnderRigidMotionAndResistsAFold()
    {
        var plan = (Sheet(3) with { Pins = [] }).Compile();
        using var solver = new ClothSolver3D(plan);
        var rest = solver.Capture();
        var transform = Matrix4x4.CreateRotationX(.7f) * Matrix4x4.CreateTranslation(2, 3, 1);
        var rigid = rest with { Positions = rest.Positions.Select(position => Vector3.Transform(position, transform)).ToImmutableArray() };
        Assert.InRange(ClothState3D.Measure(plan, rigid, new()).BendEnergy, 0, .000001f);
        var folded = rest with { Positions = rest.Positions.Select(position => position.Z > 0 ? position + new Vector3(0, .6f, 0) : position).ToImmutableArray() };
        float before = ClothState3D.Measure(plan, folded, new()).BendEnergy;
        solver.Restore(folded);
        solver.Step(new() { Gravity = Vector3.Zero, Substeps = 8, Iterations = 4 }, new());
        Assert.True(ClothState3D.Measure(plan, solver.Capture(), new()).BendEnergy < before);
    }

    [Fact]
    public void InvalidAuthoringAndForeignSnapshotsFailBeforeMutation()
    {
        Assert.Throws<ArgumentException>(() => (Sheet() with { Pins = [0, 0] }).Compile());
        Assert.Throws<ArgumentException>(() => (Sheet() with { Material = new() { WarpCompliance = -1 } }).Compile());
        var massOverflow = ClothDefinition3D.Grid("overflow", 2, 2, new(2, 2), Vector3.Zero)
            with { Material = new() { ArealDensity = float.MaxValue } };
        Assert.Throws<ArgumentException>(() => massOverflow.Compile());
        Assert.Throws<ArgumentException>(() => (Sheet() with { Indices = [0, 0, 1] }).Compile());
        var shaped = Sheet(3);
        Assert.Throws<NotSupportedException>(() => (shaped with { Positions = shaped.Positions.SetItem(4, new(0, 4, 0)) }).Compile());
        using var solver = new ClothSolver3D(Sheet().Compile());
        var snapshot = solver.Capture();
        Assert.Throws<ArgumentException>(() => solver.SetPin(5, Vector3.Zero));
        Assert.Throws<ArgumentException>(() => solver.Restore(snapshot with { ContentKey = "foreign" }));
        Assert.Throws<ArgumentException>(() => solver.Restore(snapshot with { Positions = snapshot.Positions.SetItem(0, new(float.NaN, 0, 0)) }));
        Assert.True(snapshot.Positions.SequenceEqual(solver.Capture().Positions));
        Assert.Equal(snapshot.Tick, solver.Tick);
    }

    [Theory]
    [InlineData(30, 16)]
    [InlineData(60, 8)]
    [InlineData(120, 4)]
    public void FrameRateChangesPreserveTheSameSmallStepSchedule(int rate, int substeps)
    {
        var plan = Sheet(5).Compile();
        using var baseline = new ClothSolver3D(plan);
        using var tested = new ClothSolver3D(plan);
        var contacts = new ClothContacts3D { PlaneNormal = Vector3.UnitY };
        for (int frame = 0; frame < 60; frame++)
        {
            baseline.Step(new(), contacts);
        }
        for (int frame = 0; frame < rate; frame++)
        {
            tested.Step(new() { DeltaSeconds = 1f / rate, Substeps = substeps }, contacts);
        }
        float error = baseline.Capture().Positions.Zip(tested.Capture().Positions, Vector3.Distance).Max();
        Assert.InRange(error, 0, .002f);
    }

    [Fact]
    public void FlexibleBendingControlLetsACantileverDroop()
    {
        // Two rows clamp both location and tangent; one row permits a rigid hinge rotation.
        var definition = Sheet(5) with { Pins = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9] };
        using var flexible = new ClothSolver3D(definition.Compile());
        using var stiff = new ClothSolver3D((definition with { Material = definition.Material with { BendCompliance = .0001f } }).Compile());
        var options = new ClothStepOptions3D();
        for (int frame = 0; frame < 120; frame++)
        {
            flexible.Step(options, new());
            stiff.Step(options, new());
        }
        float flexibleHeight = flexible.Capture().Positions[22].Y;
        float stiffHeight = stiff.Capture().Positions[22].Y;
        Assert.True(flexibleHeight < stiffHeight - .2f, $"Flexible tip {flexibleHeight}; stiff tip {stiffHeight}.");
    }

    [Fact]
    public void DiscreteContactDoesNotPretendToPreventHighSpeedTunneling()
    {
        var definition = ClothDefinition3D.Grid("tunnel", 2, 2, new(.1f, .1f), new(-.05f, 1, -.05f));
        var plan = definition.Compile();
        using var solver = new ClothSolver3D(plan);
        var initial = solver.Capture();
        solver.Restore(initial with { Velocities = Enumerable.Repeat(new Vector3(0, -120, 0), 4).ToImmutableArray() });
        var contacts = new ClothContacts3D { SphereCenter = Vector3.Zero, SphereRadius = .5f };
        solver.Step(new() { Gravity = Vector3.Zero, Substeps = 1, Iterations = 1 }, contacts);
        Assert.All(solver.Capture().Positions, position => Assert.True(position.Y < -.5f));
        Assert.Equal(0, ClothState3D.Measure(plan, solver.Capture(), contacts).MaximumSurfacePenetration);
    }

    [Fact]
    public void ReusableSceneAgentsHaveIndependentStateAndRenderingDoesNotStep()
    {
        var definition = new ClothAgentDefinition3D(Sheet(3).Compile());
        using var scene = SceneCompiler.Compile(Scene.World("world",
            [Scene.Agent("left", definition), Scene.Agent("right", definition, new(4, 0, 0))])).Mount();
        var left = scene.Agent<ClothSnapshot3D>("left");
        var right = scene.Agent<ClothSnapshot3D>("right");
        definition.Advance(left, new(), new());
        var projected = scene.Project();
        Assert.Equal(1, left.State.Tick);
        Assert.Equal(0, right.State.Tick);
        Assert.Equal(2, projected.Meshes.Length);
        Assert.InRange(right.State.Positions[0].X - left.State.Positions[0].X, 3.99999f, 4.00001f);
        scene.Project();
        Assert.Equal(1, left.State.Tick);
    }

    [Fact]
    public void VisualTypeScriptKernelLowersThroughTheActualComputeBackend()
    {
        byte[] spirv = ClothCompute3D.Compile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Cloth3D.v.ts")));
        Assert.True(spirv.Length > 20);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(spirv));
    }
}
