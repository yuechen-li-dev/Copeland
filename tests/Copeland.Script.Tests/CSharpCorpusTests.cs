using Copeland.Script.Codegen.CSharp;
using Copeland.Script.Mir;
using Copeland.Script.Syntax;
using Xunit;

namespace Copeland.Script.Tests;

public sealed class CSharpCorpusTests
{
    [Theory]
    [MemberData(nameof(GetCases))]
    public void CSharp_Corpus_Matches_Expected(string sourcePath)
    {
        var source = File.ReadAllText(sourcePath);
        var mir = MirLowerer.Lower(SyntaxTree.Parse(source));
        Assert.NotNull(mir.Program);
        Assert.DoesNotContain(mir.Diagnostics, d => d.Id.StartsWith("COPE-", StringComparison.Ordinal));

        var expectedPath = Path.ChangeExtension(sourcePath, ".g.cs");
        var actual = CSharpBackend.Emit(mir.Program!).SourceText;
        Assert.Equal(Normalize(File.ReadAllText(expectedPath)), Normalize(actual));
    }

    public static IEnumerable<object[]> GetCases()
    {
        var dir = Path.Combine(GetRepoRoot(), "testdata", "m0-csharp-valid");
        foreach (var sourcePath in Directory.EnumerateFiles(dir, "*.ts", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
            yield return new object[] { sourcePath };
    }

    private static string Normalize(string v) => v.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private static string GetRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(current);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
