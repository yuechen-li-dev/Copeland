namespace Copeland.TS.Tests.Corpus;

internal static class CorpusFile
{
    public static string GetRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(current);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    public static string ReadSourceText(string path)
    {
        var text = File.ReadAllText(path);
        return NormalizeLineEndings(text);
    }

    public static string GetCorpusRoot()
    {
        return Path.Combine(GetRepoRoot(), "tests", "Copeland", "Copeland.TS.Tests", "TestData", "Corpus");
    }

    public static string Normalize(string value)
    {
        return NormalizeLineEndings(value).TrimEnd();
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
