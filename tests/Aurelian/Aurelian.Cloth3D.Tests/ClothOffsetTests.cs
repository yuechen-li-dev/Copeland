using System.Collections.Immutable;
using System.Numerics;
using Xunit;

namespace Aurelian.Cloth3D.Tests;

public sealed class ClothOffsetTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    public void Vertex_bending_matches_cylinder_energy_and_has_no_flat_boundary_force(int degrees)
    {
        var definition = ClothDefinition3D.Grid("cylinder", 33, 33, new(MathF.PI, 1), Vector3.Zero);
        var rotation = Matrix4x4.CreateRotationY(degrees * MathF.PI / 180);
        definition = definition with
        {
            Positions = definition.Positions.Select(position => Vector3.Transform(position, rotation)).ToImmutableArray(),
        };
        var plan = definition.Compile();
        using var solver = new ClothSolver3D(plan);
        var rest = solver.Capture();
        Assert.InRange(ClothState3D.Measure(plan, rest, new()).BendEnergy, 0, .0001f);
        var cylinder = rest with
        {
            Positions = rest.Positions.Select(position =>
                new Vector3(MathF.Sin(position.X), 1 - MathF.Cos(position.X), position.Z)).ToImmutableArray(),
        };
        float ratio = ClothState3D.Measure(plan, cylinder, new()).BendEnergy / (MathF.PI / 2);
        Assert.InRange(ratio, .85f, 1.15f);
    }

    public static ClothDefinition3D Layers()
    {
        var first = ClothDefinition3D.Grid("first", 2, 2, new(1, 1), new(0, .05f, 0));
        var second = ClothDefinition3D.Grid("second", 2, 2, new(1, 1), Vector3.Zero);
        return new("layers", first.Positions.AddRange(second.Positions),
            first.Coordinates.AddRange(second.Coordinates), first.Indices.AddRange(second.Indices.Select(index => index + 4)))
        {
            OffsetContact = true,
        };
    }

    [Fact]
    public void Fast_layers_do_not_swap_and_rewind_replays_contact()
    {
        var plan = Layers().Compile();
        using var solver = new ClothSolver3D(plan);
        var snapshot = solver.Capture();
        snapshot = snapshot with
        {
            Velocities = Enumerable.Range(0, 8).Select(vertex => new Vector3(0, vertex < 4 ? -10 : 10, 0)).ToImmutableArray(),
        };
        solver.Restore(snapshot);
        var options = new ClothStepOptions3D { Substeps = 1, Iterations = 8, ContactIterations = 4, Gravity = Vector3.Zero };
        solver.Step(options, new());
        var expected = solver.Capture();
        Assert.True(expected.Positions.Take(4).Min(position => position.Y)
            > expected.Positions.Skip(4).Max(position => position.Y));
        solver.Restore(snapshot);
        solver.Step(options, new());
        Assert.Equal(expected.Tick, solver.Tick);
        Assert.True(expected.Positions.SequenceEqual(solver.Capture().Positions));
        Assert.True(expected.Velocities.SequenceEqual(solver.Capture().Velocities));
    }

    [Fact]
    public void Offset_contact_stops_the_existing_sphere_tunneling_case()
    {
        var plan = (ClothDefinition3D.Grid("sphere", 2, 2, new(.1f, .1f), new(-.05f, 1, -.05f)) with
        {
            OffsetContact = true,
        }).Compile();
        using var solver = new ClothSolver3D(plan);
        solver.Restore(solver.Capture() with
        {
            Velocities = Enumerable.Repeat(new Vector3(0, -120, 0), 4).ToImmutableArray(),
        });
        solver.Step(new() { Gravity = Vector3.Zero, Substeps = 1, Iterations = 1 },
            new() { SphereCenter = Vector3.Zero, SphereRadius = .5f });
        Assert.All(solver.Capture().Positions, position => Assert.True(position.Y > .5f));
    }

    [Fact]
    public void Invalid_initial_layers_fail_before_mutation()
    {
        var plan = Layers().Compile();
        using var solver = new ClothSolver3D(plan);
        var valid = solver.Capture();
        var invalid = valid with
        {
            Positions = valid.Positions.Select((position, index) => index < 4 ? position with { Y = 0 } : position).ToImmutableArray(),
        };
        Assert.Throws<ArgumentException>(() => solver.Restore(invalid));
        Assert.True(valid.Positions.SequenceEqual(solver.Capture().Positions));
    }

    [Fact]
    public void Barrier_potential_is_value_and_slope_continuous_at_the_join()
    {
        const float radius = .01f;
        var below = ClothOffsetContact3D.Potential(radius * .49999f, radius);
        var above = ClothOffsetContact3D.Potential(radius * .50001f, radius);
        Assert.InRange(MathF.Abs(below.X - above.X), 0, .000001f);
        Assert.InRange(MathF.Abs(below.Y - above.Y), 0, .0001f);
    }

    [Fact]
    public void Driven_pins_are_bounded_and_the_missed_target_is_visible()
    {
        var plan = (Layers() with { Pins = [0, 1, 2, 3] }).Compile();
        using var solver = new ClothSolver3D(plan);
        foreach (int pin in plan.Definition.Pins)
        {
            solver.SetPin(pin, plan.Definition.Positions[pin] - Vector3.UnitY);
        }
        solver.Step(new() { Substeps = 1, Gravity = Vector3.Zero }, new());
        var snapshot = solver.Capture();
        Assert.True(snapshot.Positions.Take(4).Min(position => position.Y)
            > snapshot.Positions.Skip(4).Max(position => position.Y));
        Assert.True(ClothState3D.Measure(plan, snapshot, new()).MaximumPinError > .5f);
    }

    [Fact]
    public void Piercing_initial_edges_are_rejected_even_when_vf_and_ee_clearances_are_positive()
    {
        var definition = new ClothDefinition3D("piercing",
            [new(-2, 0, -2), new(2, 0, -2), new(0, 0, 2),
                new(-.1f, -.2f, 0), new(.1f, .2f, 0), new(.1f, -.2f, .1f)],
            [new(0, 0), new(4, 0), new(2, 4), new(0, 0), new(1, 0), new(0, 1)], [0, 1, 2, 3, 4, 5])
        {
            OffsetContact = true,
        };
        using var solver = new ClothSolver3D(definition.Compile());
        Assert.Throws<ArgumentException>(() => solver.Step(new(), new()));
        Assert.Equal(0, solver.Tick);
    }

    [Fact]
    public void Offset_budget_includes_edge_edge_minima_without_a_vertex_face_collision()
    {
        var parameters = ClothOffsetContact3D.SegmentParameters(new(-1, 0, 0), new(1, 0, 0),
            new(0, .001f, -1), new(0, .001f, 1));
        Assert.Equal(.5f, parameters.X, 5);
        Assert.Equal(.5f, parameters.Y, 5);
        var definition = CrossingStrips();
        var plan = definition.Compile();
        var bounds = ClothOffsetContact3D.Bounds(plan, definition.Positions, new(), .4f);
        Assert.All(bounds, bound => Assert.InRange(bound, .0003f, .0005f));
    }

    public static ClothDefinition3D CrossingStrips()
    {
        return new("strips", [new(-1, 0, 0), new(1, 0, 0), new(1, 0, -.1f),
                new(0, .001f, -1), new(0, .001f, 1), new(.1f, .001f, 1)],
            [new(0, 0), new(2, 0), new(2, .1f), new(0, 0), new(2, 0), new(2, .1f)], [0, 1, 2, 3, 4, 5])
        {
            OffsetContact = true,
        };
    }
}
