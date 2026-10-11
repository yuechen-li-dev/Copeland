using Aetheris.Cloth3D;
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
    [Fact]
    public void RefinedPresentationUsesTheRealSceneProjectionWithoutSteppingOrChangingPhysics()
    {
        var plan = Sheet(3).Compile();
        var original = new ClothAgentDefinition3D(plan);
        var refined = original.WithPresentation(2);
        Assert.NotEqual(original.Identity, refined.Identity);
        using var scene = SceneCompiler.Compile(Scene.World("refined-cloth",
            [Scene.Agent("dress", refined, new(4, 0, 0))])).Mount();
        var agent = scene.Agent<ClothSnapshot3D>("dress");
        var before = agent.State;
        var mesh = Assert.Single(scene.Project().Meshes);
        Assert.Equal(9, agent.State.Positions.Length);
        Assert.Equal(plan.Definition.Indices.Length * 16, mesh.Indices.Length);
        Assert.True(mesh.Vertices.Length > agent.State.Positions.Length);
        Assert.Equal(refined.Identity, mesh.SourceIdentity);
        Assert.True(before.Positions.SequenceEqual(agent.State.Positions));
        Assert.Equal(0, agent.State.Tick);
        Assert.Equal(before.Positions[0], mesh.Vertices[0].Position);
        Assert.All(mesh.Vertices, vertex => Assert.InRange(MathF.Abs(vertex.Normal.Length() - 1), 0, 1e-5f));
        refined.Advance(agent, new(), new());
        Assert.Equal(1, agent.State.Tick);
        Assert.Equal(9, agent.State.Positions.Length);
        Assert.Equal(mesh.Vertices.Length, Assert.Single(scene.Project().Meshes).Vertices.Length);
        var wrongPlan = Sheet(4).Compile();
        using var wrongSolver = new ClothSolver3D(wrongPlan);
        Assert.Throws<ArgumentException>(() => ClothPresentation3D.Mesh(wrongPlan,
            wrongSolver.Capture(), refined.Material, refined.Presentation));
    }

    [Fact]
    public void Gpu_header_rejects_temporary_guides_instead_of_dropping_constraints()
    {
        var plan = Sheet().Compile();
        var contacts = new ClothContacts3D
        {
            Guides = [new("collar", [9], Vector3.UnitY, 2, .000001f)],
        };
        ClothState3D.ValidateStep(plan, new(), contacts);
        var error = Assert.Throws<NotSupportedException>(() => new ClothGpuData3D(plan).Header(
            new(), contacts, new(ClothPhase3D.Predict, 0, 1), 1));
        Assert.Contains("AUR-CLOTH-005", error.Message);
    }

    [Fact]
    public void Named_phases_preserve_the_scalar_header_and_reject_invalid_tags()
    {
        var data = new ClothGpuData3D(Sheet().Compile());
        foreach (ClothPhase3D phase in Enum.GetValues<ClothPhase3D>())
        {
            float[] header = data.Header(new(), new(), new(phase, 0, 1), 0);
            Assert.Equal((float)(int)phase, header[0]);
        }
        Assert.Equal(ClothPhase3D.Constraints, data.Constraints[0].Phase);
        Assert.Equal(ClothPhase3D.TriangleContact, data.Triangles[0].Phase);
        Assert.Throws<ArgumentOutOfRangeException>(() => data.Header(new(), new(), new((ClothPhase3D)99, 0, 1), 0));
    }

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
        Assert.Contains(plan.Constraints, constraint => constraint.Kind == ClothConstraintKind3D.VertexBend);
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
    public void Fine_rotated_planar_patterns_are_not_misclassified_as_shaped_rest()
    {
        var definition = ClothDefinition3D.Grid("rotated-flat", 129, 33, new(MathF.PI, 1), Vector3.Zero);
        var rotation = Matrix4x4.CreateRotationY(MathF.PI / 4);
        definition = definition with
        {
            Positions = definition.Positions.Select(position => Vector3.Transform(position, rotation)).ToImmutableArray(),
        };
        var plan = definition.Compile();
        using var solver = new ClothSolver3D(plan);
        float residualEnergy = ClothState3D.Measure(plan, solver.Capture(), new()).BendEnergy;
        Assert.InRange(residualEnergy, 0, .0001f);
    }

    [Fact]
    public void Folded_back_overlapping_hinges_are_not_flat_rest_patterns()
    {
        var definition = ClothDefinition3D.Grid("folded-back", 2, 2, new(1, 1), Vector3.Zero) with
        {
            BendingModel = ClothBendingModel3D.QuadraticHinge,
        };
        var hinge = definition.Compile().Constraints.Single(constraint => constraint.Kind == ClothConstraintKind3D.FlatBend);
        Vector3 origin = definition.Positions[hinge.Vertices[0]];
        Vector3 axis = Vector3.Normalize(definition.Positions[hinge.Vertices[1]] - origin);
        int opposite = hinge.Vertices[3];
        Vector3 folded = origin + Vector3.Transform(definition.Positions[opposite] - origin,
            Matrix4x4.CreateFromAxisAngle(axis, MathF.PI));
        Assert.Throws<NotSupportedException>(() => (definition with
        {
            Positions = definition.Positions.SetItem(opposite, folded),
        }).Compile());
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
    public void GarmentAgentsRetainArrangementSnapshotAndRigidPlacement()
    {
        var definition = Sheet(3) with
        {
            InitialPositions = Sheet(3).Positions.Select(point => point + new Vector3(0, 0, 2)).ToImmutableArray(),
        };
        var plan = definition.Compile();
        using var solver = new ClothSolver3D(plan);
        solver.Step(new() { Gravity = Vector3.Zero }, new());
        var snapshot = solver.Capture();
        var artifact = new ClothGarmentArtifact3D(plan, snapshot, new string('a', 64), "test.firmament");
        var agent = ClothAgentDefinition3D.FromGarment(artifact);
        using var scene = SceneCompiler.Compile(Scene.World("world",
            [Scene.Agent("garment", agent, new(4, 0, 0))])).Mount();
        var state = scene.Agent<ClothSnapshot3D>("garment").State;
        Assert.Equal(snapshot.Tick, state.Tick);
        for (int vertex = 0; vertex < state.Positions.Length; vertex++)
        {
            var position = snapshot.Positions[vertex];
            var velocity = snapshot.Velocities[vertex];
            var target = snapshot.PinTargets[vertex];
            Assert.Equal(new Vector3(position.X + 4, position.Z, -position.Y), state.Positions[vertex]);
            Assert.Equal(new Vector3(velocity.X, velocity.Z, -velocity.Y), state.Velocities[vertex]);
            Assert.Equal(new Vector3(target.X + 4, target.Z, -target.Y), state.PinTargets[vertex]);
        }
        var arranged = new ClothAgentDefinition3D(plan);
        using var initialScene = SceneCompiler.Compile(Scene.World("world",
            [Scene.Agent("arranged", arranged)])).Mount();
        Assert.True(definition.InitialPositions.SequenceEqual(initialScene.Agent<ClothSnapshot3D>("arranged").State.Positions));
    }

    [Fact]
    public void CurvedQuadGarmentRetainsRestMetricAcrossArtifactAxesAndAgentPlacement()
    {
        var patch = ClothQuadSurface3D.Grid("curved-agent", 4, 4, new(1, 1), Vector3.Zero);
        var definition = patch with
        {
            Positions = patch.Positions.Select(point => point with { Y = .1f * MathF.Sin(point.X * 3) }).ToImmutableArray(),
            Surface = patch.Surface! with { Rest = ClothSurfaceRest3D.FittedSurface },
            Material = new() { WarpCompliance = .001f, WeftCompliance = .004f,
                DiagonalCompliance = .002f, BendCompliance = .01f },
        };
        var plan = definition.Compile();
        var artifact = new ClothGarmentArtifact3D(plan, null, new string('a', 64), "surface/curved-agent");
        var agentDefinition = ClothAgentDefinition3D.FromGarment(artifact);
        using var scene = SceneCompiler.Compile(Scene.World("world",
            [Scene.Agent("garment", agentDefinition, new(4, 2, -1))])).Mount();
        var agent = scene.Agent<ClothSnapshot3D>("garment");
        var initial = agent.State;
        Assert.All(plan.Constraints, constraint =>
            Assert.InRange(Math.Abs(ClothSurfaceMechanics3D.Evaluate(constraint, initial.Positions).Residual), 0, .0001f));
        agentDefinition.Advance(agent, new() { Gravity = Vector3.Zero, Substeps = 2, Iterations = 8 }, new());
        Assert.InRange(initial.Positions.Zip(agent.State.Positions, Vector3.Distance).Max(), 0, .0001f);
        long tick = agent.State.Tick;
        scene.Project();
        Assert.Equal(tick, agent.State.Tick);
    }

    [Fact]
    public void VisualTypeScriptKernelLowersThroughTheActualComputeBackend()
    {
        byte[] spirv = ClothCompute3D.Compile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Cloth3D.v.ts")));
        Assert.True(spirv.Length > 20);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(spirv));
    }
}
