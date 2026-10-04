namespace Machina.Core.Authoring;

/// <summary>Renderer-neutral table geometry shared with the notebook's hosted table.</summary>
public static class UiTableLayout
{
    public const int WidthSampleSize = 32;
    public const double RowHeight = 34;

    public static double PreferredColumnWidth(int characters, double minimum = 180, double maximum = 320)
    {
        return Math.Clamp(28 + characters * 7.2, minimum, maximum);
    }
}
