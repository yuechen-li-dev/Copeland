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
    public void Nominal_union_corpus_matches_expected_javascript_and_pinned_hash()
    {
        string repoRoot = GetRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "tests", "Copeland", "Copeland.TS.Tests", "TestData", "Corpus", "cts-union-m0b", "nominal-union.ts");
        string expectedPath = Path.ChangeExtension(sourcePath, ".g.js");
        var mir = MirLowerer.Lower(SyntaxTree.Parse(File.ReadAllText(sourcePath)));

        Assert.Empty(mir.Diagnostics);
        Assert.NotNull(mir.Program);
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(mir.Program!);
        Assert.True(emitted.Success);
        Assert.Equal(File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal), emitted.SourceText);
        Assert.Equal("BBAAA7FA856306904D74F64947A072BFA80958A46DA8C8E274660E7ABB37AAEC", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expectedPath))));
    }

    [Fact]
    public void Pure_class_corpus_matches_expected_javascript_and_pinned_hash()
    {
        string repoRoot = GetRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "tests", "Copeland", "Copeland.TS.Tests", "TestData", "Corpus", "cts-class-m1", "main.ts");
        string expectedPath = Path.ChangeExtension(sourcePath, ".g.js");
        var mir = MirLowerer.Lower(SyntaxTree.Parse(File.ReadAllText(sourcePath)));

        Assert.Empty(mir.Diagnostics);
        Assert.NotNull(mir.Program);
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(mir.Program!);
        Assert.True(emitted.Success);
        Assert.Equal(File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal), emitted.SourceText);
        Assert.Equal("9620114CDEA686AFDF1F5D7F6BE5C6F7150B1C4348E08B7A4CD39D4BE60CE135", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expectedPath))));
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
    [InlineData("payload-enum-match.g.js", "C62AF03259B14E94A41B5D6F58696E0E25F0E5CAA76DA31C63531934EE9FE1EC")]
    [InlineData("nominal-enum-types.g.js", "242874B3B43AB5A206FDDFF81748B1BE3F80A19AD62864C74A99A0FF4E7B36A3")]
    [InlineData("result-construction-match.g.js", "37CDB992A39F0EE1E983DB4A7684766A22713492166F5DC56E4453F2DCEF0E59")]
    [InlineData("result-propagation.g.js", "B87568C95250D9415E1FDBB2DC46D4CD6FA893F8C8320D642E4E53B595607865")]
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

        Assert.Equal("5F29CFB51D5119BF71F44F580FFAEEA3B5D451246A7A1FF42AB425F8D0803248", hash);
    }

    [Theory]
    [InlineData("m2-table-basic.g.js", "29FB270F855AC7A704C9A4ABD00105EFB152EAA210DEBED850402E31DBBA9C81")]
    [InlineData("m2-table-nested.g.js", "215EC9852AA6F18E6651897DEE375182B5F6F64675AD0A25617AF8C90CBBAA54")]
    public void Table_Artifacts_Have_Stable_Hashes(string fileName, string expectedHash)
    {
        string artifactPath = Path.Combine(GetCorpusRoot(), fileName);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(artifactPath)));

        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData("record-basic.g.js", "3B67FE70D2912123FCF6BEF4D5D1ADA0C9B1D21F3F931335CD1405589BA473D0")]
    [InlineData("record-order-with.g.js", "A2A34C7F2CFEF31AA2186740C799249770D7050BE3C7FDD9052F8FE99D7BE146")]
    [InlineData("record-result-enum.g.js", "8DEACC0FE023E665B4C777CBA364D2262673DB6F65EACDC9C548EA7014A64732")]
    [InlineData("record-try-except.g.js", "E246048D818F5F6FF7DF932C546F112D7E4889C9A6CDB49EAB238BE47F0D7903")]
    [InlineData("inferred-reuse.g.js", "2A620DE6C9EAA21AC2DA56512A60DC8200F231CB34BBE245AA6516E6CFEE3EE5")]
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

    private static string GetRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
