using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Lowering;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class JavaScriptCorpusTests
{
    [Fact]
    public void Corpus_Matches_Expected_JavaScript_Byte_For_Byte()
    {
        string sourcePath = Path.Combine(GetCorpusRoot(), "main-returns-42.ts");
        string expectedPath = Path.ChangeExtension(sourcePath, ".g.js");
        var mir = MirLowerer.Lower(SyntaxTree.Parse(File.ReadAllText(sourcePath)));

        Assert.Empty(mir.Diagnostics);
        Assert.NotNull(mir.Program);

        JavaScriptCompilation result = JavaScriptBackend.Emit(mir.Program);

        Assert.True(result.Success);
        Assert.Equal(File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal), result.SourceText);
    }

    private static string GetCorpusRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string root = Path.Combine(directory.FullName, "TestData", "Corpus");
            if (Directory.Exists(root))
            {
                return root;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate JavaScript backend corpus.");
    }
}
