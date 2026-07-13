using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Lowering;
using Copeland.TS.Syntax;
using System.Security.Cryptography;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class JavaScriptCorpusTests
{
    [Fact]
    public void Corpus_Matches_Expected_JavaScript_Byte_For_Byte()
    {
        foreach (string sourcePath in Directory.EnumerateFiles(GetCorpusRoot(), "*.ts", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.Ordinal))
        {
            string expectedPath = Path.ChangeExtension(sourcePath, ".g.js");
            var mir = MirLowerer.Lower(SyntaxTree.Parse(File.ReadAllText(sourcePath)));

            Assert.Empty(mir.Diagnostics);
            Assert.NotNull(mir.Program);

            JavaScriptCompilation first = JavaScriptBackend.Emit(mir.Program);
            JavaScriptCompilation second = JavaScriptBackend.Emit(mir.Program);

            Assert.True(first.Success);
            Assert.Equal(first.SourceText, second.SourceText);
            Assert.DoesNotContain("\r", first.SourceText, StringComparison.Ordinal);
            Assert.Equal(File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal), first.SourceText);
        }
    }

    [Fact]
    public void Primitive_Equality_Artifact_Has_Stable_Hash()
    {
        string artifactPath = Path.Combine(GetCorpusRoot(), "primitive-equality.g.js");
        byte[] bytes = File.ReadAllBytes(artifactPath);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));

        Assert.Equal("AD297686E173C5A30FD9D6CFA030F90DC048D604CFB7808063DED441EC74B5FC", hash);
    }

    [Theory]
    [InlineData("payload-enum-match.g.js", "C7FAD5A76AB26FF93396BE8038D496B70236B49B6316BCEB43F1ACE8DE59AD79")]
    [InlineData("nominal-enum-types.g.js", "EA992B0D572259A139FE56F785487D67F111AFDBC666FB89ADA097F04B9BE4FD")]
    [InlineData("result-construction-match.g.js", "E41DADDE7417A84A81F8A20CF22EE849B182703F1743190A89510310D0C32974")]
    [InlineData("result-propagation.g.js", "63734BDEE21591612CF1D6A1B064CC445F130E5D35DA36C038F5275E8ECDDE3F")]
    public void Payload_Enum_Artifacts_Have_Stable_Hashes(string fileName, string expectedHash)
    {
        string artifactPath = Path.Combine(GetCorpusRoot(), fileName);
        byte[] bytes = File.ReadAllBytes(artifactPath);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));

        Assert.Equal(expectedHash, hash);
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
