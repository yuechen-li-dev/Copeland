namespace Machina.ComponentGallery.Sample;

internal static class GalleryFontFixturePaths
{
    public static string ResolveCrimsonTextPath()
    {
        return ResolveRequiredFontPath("CrimsonText-Regular.ttf");
    }

    public static string ResolveSpaceMonoPath()
    {
        return ResolveRequiredFontPath("SpaceMono-Regular.ttf");
    }

    private static string ResolveRequiredFontPath(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The required gallery proof font fixture was not found. Expected {fileName} in the sample output.",
                path);
        }

        return path;
    }
}
