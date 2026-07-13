namespace Copeland.TS.Backend.CSharp.Tests;

internal static class CorpusFile
{
    public static string GetCorpusRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.TS.slnx")))
            {
                return Path.Combine(
                    directory.FullName,
                    "tests",
                    "Copeland",
                    "Copeland.TS.Tests",
                    "TestData",
                    "Corpus");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Copeland TS corpus root.");
    }

    public static string ReadSourceText(string path)
    {
        return NormalizeLineEndings(File.ReadAllText(path));
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
