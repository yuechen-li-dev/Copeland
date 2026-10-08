using Machina.Layout.Geometry;
using Machina.Standard.Components;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static class TinyFarmStatsScrollbar
{
    public const int RowHeight = 34;
    public const int ViewportHeight = RowHeight * TinyFarmMenus.PageSize;

    public static ScrollbarGeometry Geometry(int count, int offset) => ScrollRegion.ComputeScrollbarGeometry(
        new Rect(1180, 254, 24, ViewportHeight), count * RowHeight, ViewportHeight, offset * RowHeight);
}
