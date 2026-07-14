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

    [Fact]
    public void Try_Except_Artifact_Has_Stable_Hash()
    {
        string artifactPath = Path.Combine(GetCorpusRoot(), "try-except-success.g.js");
        byte[] bytes = File.ReadAllBytes(artifactPath);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));

        Assert.Equal("DD678A23F507736CE1E54FFAA0124158EF2F5A5B833B441F87B1705F02FF4BA7", hash);
    }

    [Theory]
    [InlineData("m2-table-basic.g.js", "B9AEA6132233229C4F594E9AB34F89F9D4E8F906B160CC1485CE2706436E3C26")]
    [InlineData("m2-table-nested.g.js", "7D72CC23337D65B4F1841D01B5E7E7ED04BD65794109F3D43FB54EEDF3856145")]
    public void Table_Artifacts_Have_Stable_Hashes(string fileName, string expectedHash)
    {
        string artifactPath = Path.Combine(GetCorpusRoot(), fileName);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(artifactPath)));

        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData("record-basic.g.js", "AA91167AF8D33B45731748BF5D0861FBCE4EF7D195E96E2ADFFB7C77F62EB8A0")]
    [InlineData("record-order-with.g.js", "EC92548B37415D888B02ACB6C9D163096DD2D46FF66C23767E5BE0E43DA56060")]
    [InlineData("record-result-enum.g.js", "DDACF318CB2777D5A4E5A138B8875F3AB3752F8AD93D6C64DD185EF55B56BB24")]
    [InlineData("record-try-except.g.js", "859A7CD39986AC6D3410943A529AAA0222E320240FFE96D36A2E2883DC733F7D")]
    public void Record_Artifacts_Have_Stable_Hashes(string fileName, string expectedHash)
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
