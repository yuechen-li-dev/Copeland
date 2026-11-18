using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Strategy;
using Xunit;

namespace Riverward.Tests;

public sealed class CityTests
{
    [Fact]
    public void SharedEdgesAndProjectedPicksAgreeAfterOrbitZoomAndResize()
    {
        var view = new CityView();
        HexCell cell = new(-1, 0);
        foreach (Vector2 surface in new Vector2[] { new(1280, 800), new(1537, 864), new(639, 482) })
        {
            view.Move(0, 0, 1, 1, .3f);
            for (int side = 0; side < HexCell.SideCount; side++)
            {
                HexEdge edge = cell.Edge(side);
                Assert.Equal(edge, cell.Neighbor(side).Edge((side + 3) % HexCell.SideCount));
                Vector2 midpoint = (edge.A.Position(CityMap.Radius) + edge.B.Position(CityMap.Radius)) * .5f;
                Vector2 point = Vector2.Lerp(midpoint, cell.Center(CityMap.Radius), .025f);
                Assert.True(Aurelian.Runtime.Camera3D.TryProject(new(point.X, 0, point.Y),
                    view.Camera(surface.X / surface.Y).Matrix, surface, out Vector2 pixel));
                CityTarget picked = Assert.IsType<CityTarget>(view.Pick(pixel, surface, Tool.Streets));
                Assert.Equal(cell, picked.Cell);
                Assert.Equal(edge, picked.Edge);
            }
        }
    }

    [Fact]
    public void SameCostParkDoublesCapacityOnlyWhenItTouchesBothBlocks()
    {
        var city = new City();
        CityReport initial = city.Report;
        Placement prime = city.Preview(Tool.Park, new CityTarget(new(-1, 0), default));
        Placement quiet = city.Preview(Tool.Park, new CityTarget(new(2, 0), default));
        Assert.Equal(520, city.Cash);
        Assert.Same(initial, city.Report);
        Assert.Equal(16, initial.Housing);
        Assert.Equal(16, quiet.Forecast!.Housing);
        Assert.Equal(32, prime.Forecast!.Housing);
        Assert.Equal(2, prime.Improved!.Length);
        Place(city, Tool.Park, new(-1, 0));
        Assert.Equal(prime.Forecast.Blocks.ToArray(), city.Report.Blocks.ToArray());
        Assert.Equal(455, city.Cash);
        Assert.False(city.Build(Tool.Workshop, new CityTarget(new(-1, 0), default)).Allowed);
        Assert.Equal(455, city.Cash);
        Placement noise = city.Preview(Tool.Workshop, new CityTarget(new(-1, 1), default));
        Assert.Equal(24, noise.Forecast!.Housing);
        Assert.Equal(new[] { CityMap.SecondHomes }, noise.Worsened);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TownCanWinWithApartmentsOrWithMoreLand(bool densify)
    {
        var city = new City();
        HexCell secondWorkshop = new(1, 1);
        Place(city, Tool.Workshop, secondWorkshop);
        Assert.Equal(14, city.Report.Jobs);
        Street(city, new(1, 0), 1);
        Street(city, new(1, 0), 0);
        Assert.Equal(28, city.Report.Jobs);

        Street(city, new(-2, 0), 0);
        Street(city, new(-2, 0), 1);
        HexCell westernBlock = new(-3, 1);
        if (densify)
        {
            Place(city, Tool.Park, new(-1, 0));
            Place(city, Tool.Market, westernBlock);
            Assert.Equal(10, city.Report.Blocks[westernBlock].Customers);
        }
        else
        {
            Place(city, Tool.Homes, westernBlock);
            Place(city, Tool.Market, new(-1, 0));
        }
        for (int tick = 0; tick < City.MonthTicks * 8; tick += 3)
        {
            city.Advance(3);
        }
        Assert.True(city.Won);
        Assert.Equal(densify ? 28 : 24, city.Report.Employed);
        Assert.Equal(densify, city.Report.Blocks[CityMap.FirstHomes].Residents > 8);
    }

    [Fact]
    public void SecondDockConnectsAnIsolatedStreetAndSendsVisitorsToItsFishMarket()
    {
        var city = new City();
        HexCell origin = new(-1, 0);
        HexCell destination = new(1, -3);
        HexCell fishMarket = new(1, -2);
        HexCell workshop = new(2, -3);
        Assert.False(city.Preview(Tool.Dock, new(new HexCell(-3, 0), default)).Allowed);
        Place(city, Tool.Dock, origin);
        Street(city, destination, 5);
        Place(city, Tool.Workshop, workshop);
        Place(city, Tool.FishMarket, fishMarket);
        Assert.Equal(14, city.Report.Jobs);
        Assert.Empty(city.Report.FerryStops);
        Assert.Equal(-18, city.Report.Blocks[fishMarket].Net);

        Placement preview = city.Preview(Tool.Dock, new(destination, default));
        Assert.True(preview.Allowed);
        Assert.Equal(28, preview.Forecast!.Jobs);
        Assert.Equal(2, preview.Forecast.FerryStops.Length);
        Assert.Equal(10, preview.Forecast.FerryTrips);
        Assert.Equal(52, preview.Forecast.Blocks[fishMarket].Net);
        Assert.Contains(workshop, preview.Improved!);
        Assert.Equal(14, city.Report.Jobs);

        Place(city, Tool.Dock, destination);
        Assert.Equal(preview.Forecast.Blocks.ToArray(), city.Report.Blocks.ToArray());
        HexEdge frontage = destination.Edge(5);
        Placement demolition = city.Build(Tool.Clear, new(destination, frontage, Street: true));
        Assert.True(demolition.Allowed);
        Assert.Equal(14, city.Report.Jobs);
        Assert.Equal(0, city.Report.FerryTrips);
        Assert.Equal(-18, city.Report.Blocks[fishMarket].Net);
        Assert.Contains(fishMarket, demolition.Worsened!);
    }

    [Fact]
    public void BoardwalkRelievesUnhappinessAndSharesTheFerryVisitorMarket()
    {
        var city = new City();
        HexCell origin = new(-1, 0);
        HexCell destination = new(1, -3);
        HexCell fishMarket = new(1, -2);
        HexCell touristBoardwalk = new(1, -4);
        Place(city, Tool.Dock, origin);
        Street(city, destination, 5);
        Place(city, Tool.Dock, destination);
        Place(city, Tool.FishMarket, fishMarket);
        int before = city.Report.Happiness;
        Place(city, Tool.Boardwalk, new(-1, 1));
        Assert.Equal(18, city.Report.Blocks[CityMap.SecondHomes].Happiness - before);
        Assert.Equal(24, city.Report.Housing);
        Place(city, Tool.Boardwalk, touristBoardwalk);
        Assert.Equal(10, city.Report.FerryTrips);
        Assert.Equal(5, city.Report.Blocks[fishMarket].Trips);
        Assert.Equal(5, city.Report.Blocks[touristBoardwalk].Trips);
        Assert.Equal(-2, city.Report.Blocks[touristBoardwalk].Net);
    }

    [Fact]
    public void ReloadPreservesConnectedStreetIdentityAndNextMonth()
    {
        var city = new City();
        Street(city, new(1, 0), 1);
        Street(city, new(1, 0), 0);
        Place(city, Tool.Workshop, new(1, 1));
        city.Advance(3);
        byte[] saved = JsonSerializer.SerializeToUtf8Bytes(city.Capture(), RiverwardJson.Default.CitySnapshot);
        CitySnapshot snapshot = JsonSerializer.Deserialize(saved, RiverwardJson.Default.CitySnapshot)!;
        City.Validate(snapshot);
        var loaded = new City();
        loaded.Restore(snapshot);
        Assert.Equal(saved, JsonSerializer.SerializeToUtf8Bytes(loaded.Capture(), RiverwardJson.Default.CitySnapshot));
        for (int tick = 3; tick < City.MonthTicks; tick += 3)
        {
            city.Advance(3);
            loaded.Advance(3);
        }
        Assert.Equal(city.Cash, loaded.Cash);
        Assert.Equal(city.Report.Jobs, loaded.Report.Jobs);
        Assert.Equal(city.Report.Blocks.ToArray(), loaded.Report.Blocks.ToArray());
    }

    private static void Street(City city, HexCell cell, int side) =>
        Assert.True(city.Build(Tool.Streets, new CityTarget(cell, cell.Edge(side), Street: true)).Allowed);

    private static void Place(City city, Tool tool, HexCell cell) =>
        Assert.True(city.Build(tool, new CityTarget(cell, default)).Allowed);
}
