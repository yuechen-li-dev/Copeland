using Machina.Layout.Geometry;
using Xunit;

namespace Machina.Layout.Tests;

public sealed class LinearFitTests
{
    [Fact]
    public void FourStripsLeaveOneUnplacedIn169Pixels()
    {
        var placements = LinearFit.Place([new(44), new(44), new(44), new(44)], [new(20, 169)]);

        Assert.Equal([new(0, 20, 44), new(0, 64, 44), new(0, 108, 44)], placements);
    }

    [Fact]
    public void WrapsInOrderAndReservesSpaceOnlyInTheLastLane()
    {
        var placements = LinearFit.Place([new(18), new(18, 5), new(21, 10), new(16), new(1)],
            [new(10, 44), new(70, 49)], gap: 3, trailingReserve: 10);

        Assert.Equal([new(0, 10, 18), new(0, 36, 18), new(1, 70, 21)], placements);
    }

    [Fact]
    public void DoesNotSkipAnOversizedItemToPlaceLaterItems()
    {
        Assert.Empty(LinearFit.Place([new(20), new(1)], [new(0, 19)]));
    }
}
