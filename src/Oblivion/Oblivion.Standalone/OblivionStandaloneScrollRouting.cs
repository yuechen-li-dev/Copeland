namespace Oblivion.Standalone;

public enum OblivionStandaloneScrollOwner
{
    Page,
    Document,
}

public static class OblivionStandaloneScrollRouting
{
    public static OblivionStandaloneScrollOwner ResolveOwner(
        double documentExtent,
        double documentViewport,
        double documentOffset,
        double deltaY)
    {
        double maximumOffset = Math.Max(0, documentExtent - documentViewport);
        if (deltaY < 0 && documentOffset < maximumOffset)
        {
            return OblivionStandaloneScrollOwner.Document;
        }

        if (deltaY > 0 && documentOffset > 0)
        {
            return OblivionStandaloneScrollOwner.Document;
        }

        return OblivionStandaloneScrollOwner.Page;
    }

    public static double ComputePageOffset(
        double currentOffset,
        double pageExtent,
        double pageViewport,
        double deltaY)
    {
        double maximumOffset = Math.Max(0, pageExtent - pageViewport);
        double requestedOffset = currentOffset - (deltaY * 48);
        return Math.Clamp(requestedOffset, 0, maximumOffset);
    }
}
