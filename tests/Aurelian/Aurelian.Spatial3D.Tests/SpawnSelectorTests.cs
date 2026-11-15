using System.Numerics;
using Xunit;

namespace Aurelian.Spatial3D.Tests;

public sealed class SpawnSelectorTests
{
    private static SpatialWorld3D World() => new([
        new("floor", CollisionMesh3D.Box(new(20, .5f, 20)), Matrix4x4.CreateTranslation(0, -.5f, 0)),
        new("cover", CollisionMesh3D.Box(new(1, 1.5f, .2f)), Matrix4x4.CreateTranslation(0, 1.5f, -5)),
    ]);

    [Fact]
    public void ProposalsPreferCoverPenalizeRecentUseAndDoNotCommitUntilAccepted()
    {
        var selector = new SpawnSelector3D(new() { ReusePenalty = 1 });
        SpawnPoint3D[] points = [new("covered", new(0, 0, -10)), new("exposed", new(8, 0, -10))];
        Vector3 eye = new(0, 1.6f, 0);
        var choice = selector.Select(World(), points, eye, -Vector3.UnitZ, 0)!;
        Assert.Equal("covered", choice.Point.Id);
        Assert.False(choice.Visible);
        Assert.Empty(selector.Capture());
        selector.RecordUse(choice.Point.Id, 0);
        Assert.Equal("exposed", selector.Select(World(), points.Reverse().ToArray(), eye, -Vector3.UnitZ, 1)!.Point.Id);
        Assert.Equal("covered", selector.Select(World(), points, eye, -Vector3.UnitZ, 21)!.Point.Id);
        var replay = new SpawnSelector3D(new() { ReusePenalty = 1 });
        replay.Restore(selector.Capture());
        Assert.Equal(selector.Select(World(), points, eye, -Vector3.UnitZ, 3),
            replay.Select(World(), points, eye, -Vector3.UnitZ, 3));
    }

    [Fact]
    public void UnsafeOccupiedUnsupportedAndTooCloseCandidatesAreRejected()
    {
        var selector = new SpawnSelector3D();
        SpawnPoint3D[] points = [new("close", new(2, 0, 0)), new("inside", new(0, 0, -5)),
            new("unsupported", new(30, 0, 0)), new("occupied", new(-10, 0, 0))];
        Assert.Null(selector.Select(World(), points, new(0, 1.6f, 0), -Vector3.UnitZ, 0, [new(-10, 0, 0)]));
    }

    [Fact]
    public void TiesUseStableIdentitiesAndInvalidHistoryCannotPartiallyRestore()
    {
        var selector = new SpawnSelector3D();
        SpawnPoint3D[] points = [new("b", new(-10, 0, 5)), new("a", new(10, 0, 5))];
        Assert.Equal("a", selector.Select(World(), points, new(0, 1.6f, 0), -Vector3.UnitZ, 0)!.Point.Id);
        selector.RecordUse("a", 2);
        Assert.Throws<ArgumentException>(() => selector.Restore([new("b", 0), new("b", 1)]));
        Assert.Equal(new SpawnUse3D("a", 2), Assert.Single(selector.Capture()));
        Assert.Throws<ArgumentOutOfRangeException>(() => selector.Select(World(), points, new(0, 1.6f, 0), -Vector3.UnitZ, 1));
    }
}
