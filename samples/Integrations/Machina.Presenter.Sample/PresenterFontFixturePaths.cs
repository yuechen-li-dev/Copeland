namespace Machina.Presenter.Sample;

internal static class PresenterFontFixturePaths
{
    public static string ResolveCrimsonTextPath()
    {
        return ResolveRequiredFontPath("CrimsonText-Regular.ttf");
    }

    private static string ResolveRequiredFontPath(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The required presenter proof font fixture was not found. Expected {fileName} in the sample output.",
                path);
        }

        return path;
    }
}
