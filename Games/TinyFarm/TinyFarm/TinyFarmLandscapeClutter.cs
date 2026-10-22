using TinyFarm.Core;

namespace TinyFarm.Native;

internal sealed partial class TinyFarmWorldPresenter
{
    private void AddLandscapeClutter(TinyFarmFrame frame, TimeSpan elapsed)
    {
        if (frame.ActiveScene == TinyFarmSceneIds.Riverside)
        {
            double[] groups = [.25, 1.65, 3.8, 5.45, 7.65, 9.7];
            for (int index = 0; index < groups.Length; index++)
            {
                double y = groups[index];
                ShoreGroup(index, y, east: false, elapsed);
                ShoreGroup(index + 10, y + .65, east: true, elapsed);
            }
            AddSlot("river-path-grass-a", "prop.bank-grass", 2.4, 6.55, elapsed, .9);
            AddSlot("river-path-grass-b", "prop.bank-grass", 4.65, 7.5, elapsed, .85, mirror: true);
            AddSlot("river-path-grass-c", "prop.bank-grass", 6.25, 8.65, elapsed, 1.05);
            AddSlot("river-path-rocks", "prop.bank-rocks", 5.1, 8.7, elapsed, .7);
        }
        else if (frame.ActiveScene == TinyFarmSceneIds.Farm)
        {
            var road = TinyFarmLandscape.FarmRoads[0];
            for (int index = 12; index < road.Centerline.Count - 6; index += 7)
            {
                var at = road.Centerline[index];
                var next = road.Centerline[index + 1];
                double dx = next.X - at.X;
                double dy = next.Y - at.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                double side = index % 2 == 0 ? .68 : -.68;
                double x = at.X - dy / length * side;
                double y = at.Y + dx / length * side + .12;
                AddSlot("farm-path-grass-" + index, "prop.bank-grass", x, y, elapsed,
                    .65 + (index % 3) * .15, mirror: index % 2 == 0);
            }
            AddSlot("farm-path-stones", "prop.bank-rocks", 12.65, 7.6, elapsed, .6);
        }
    }

    private void ShoreGroup(int index, double y, bool east, TimeSpan elapsed)
    {
        double x = east ? TinyFarmLandscape.EastBank(y) : TinyFarmLandscape.WestBank(y);
        double drySide = east ? 1 : -1;
        string id = "shore-cluster-" + index;
        AddSlot(id + "-stones", "prop.bank-rocks", x - drySide * .06, y + .2, elapsed,
            .8 + (index % 3) * .16, mirror: index % 2 == 0);
        AddSlot(id + "-grass", "prop.bank-grass", x + drySide * .42, y + .36, elapsed,
            1 + (index % 3) * .14, mirror: index % 2 != 0);
        if (index % 3 != 1)
        {
            AddSlot(id + "-reeds", "object.reeds", x + drySide * .16, y + .12, elapsed,
                .74 + (index % 3) * .12, mirror: east);
        }
    }
}
