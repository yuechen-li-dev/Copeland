using System.Security.Cryptography;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class CallableCorpusTests
{
    [Fact]
    public void Callable_reference_corpus_is_byte_stable_in_all_emission_profiles()
    {
        string directory = Path.Combine(FindRepoRoot(), "tests", "Copeland", "Copeland.TS.Tests", "TestData", "Corpus", "cts-call-m0b");
        string source = File.ReadAllText(Path.Combine(directory, "main.ts"));
        var mir = MirLowerer.Lower(SyntaxTree.Parse(source));

        Assert.Empty(mir.Diagnostics);
        Assert.NotNull(mir.Program);
        Assert.Equal(NormalizeNewlines(File.ReadAllText(Path.Combine(directory, "main.cope"))), NormalizeNewlines(MirTextWriter.Write(mir.Program!)));
        Assert.Equal(NormalizeNewlines(File.ReadAllText(Path.Combine(directory, "main.g.cs"))), NormalizeNewlines(CSharpBackend.Emit(mir.Program).SourceText!));
        Assert.Equal(NormalizeNewlines(File.ReadAllText(Path.Combine(directory, "main.g.js"))), NormalizeNewlines(JavaScriptBackend.Emit(mir.Program).SourceText!));
        Assert.Equal(NormalizeNewlines(File.ReadAllText(Path.Combine(directory, "main.sym.js"))), NormalizeNewlines(JavaScriptBackend.Emit(mir.Program, new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic }).SourceText!));

        AssertArtifact(directory, "main.cope", 865, "677CDF3157BAB9B1FD310D33727BBC5094901BF99505A69C35C25BF42E8F0C93");
        AssertArtifact(directory, "main.g.cs", 1480, "8DD27E8377923BC74A81EB2662D98D98169DE13041F35A8D63BCE5103404A945");
        AssertArtifact(directory, "main.g.js", 1546, "E2DF6970403EDB9A74E758655DCEA5ECAFE76C286B25F3658AD916177DE0E77E");
        AssertArtifact(directory, "main.sym.js", 1508, "B6AD9D99353FBBA8FCFFD6F546581DA688E4DE49A0FC7AD40AB99AAF43712E1A");
    }

    private static void AssertArtifact(string directory, string name, int byteLength, string sha256)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, name));
        Assert.Equal(byteLength, bytes.Length);
        Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static string NormalizeNewlines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx"))) return directory.FullName;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
