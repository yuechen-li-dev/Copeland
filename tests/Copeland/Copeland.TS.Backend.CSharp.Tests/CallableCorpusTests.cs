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
    public void Complete_callable_corpus_is_byte_stable_in_all_emission_profiles()
    {
        string directory = Path.Combine(FindRepoRoot(), "tests", "Copeland", "Copeland.TS.Tests", "TestData", "Corpus", "cts-call-m1");
        string source = File.ReadAllText(Path.Combine(directory, "main.ts"));
        var mir = MirLowerer.Lower(SyntaxTree.Parse(source));

        Assert.Empty(mir.Diagnostics);
        Assert.NotNull(mir.Program);
        Assert.Equal(NormalizeNewlines(File.ReadAllText(Path.Combine(directory, "main.cope"))), NormalizeNewlines(MirTextWriter.Write(mir.Program!)));
        Assert.Equal(NormalizeNewlines(File.ReadAllText(Path.Combine(directory, "main.g.cs"))), NormalizeNewlines(CSharpBackend.Emit(mir.Program).SourceText!));
        Assert.Equal(NormalizeNewlines(File.ReadAllText(Path.Combine(directory, "main.g.js"))), NormalizeNewlines(JavaScriptBackend.Emit(mir.Program).SourceText!));
        Assert.Equal(NormalizeNewlines(File.ReadAllText(Path.Combine(directory, "main.sym.js"))), NormalizeNewlines(JavaScriptBackend.Emit(mir.Program, new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic }).SourceText!));

        AssertArtifact(directory, "main.cope", 2056, "1598D5EC6CF78C9743A72EBAC1D0EA85F5676487251C6FE57230CABE78343F4B");
        AssertArtifact(directory, "main.g.cs", 5062, "0C1FB55CFCC47E9E05BE677C53E38D9FA3C61AF3A32C3411E5C44E4C7326BA2A");
        AssertArtifact(directory, "main.g.js", 9996, "C8E51A4ACBD05F13466B5AD53BFF4F521007B51677BBED1D450EEDEDE4F29F96");
        AssertArtifact(directory, "main.sym.js", 8491, "DD3B74099671F583A268D3EE8E0293813551FD8877AC8A869EE3164B1FB7C7AE");
    }

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

        AssertArtifact(directory, "main.cope", 837, "1D3BCC53723644A6823A5D8D883670B92A30803154040E5E9C008D59A327A922");
        AssertArtifact(directory, "main.g.cs", 1480, "8DD27E8377923BC74A81EB2662D98D98169DE13041F35A8D63BCE5103404A945");
        AssertArtifact(directory, "main.g.js", 2513, "4DC871B3851E4C04BACE3AE492EE7DFC8D0678727B0E111C59BF8B916FCFCFDD");
        AssertArtifact(directory, "main.sym.js", 2439, "777A9E3F68D2F1EBDB53753B3C903251D7DCC656858409916D55B4BB47E56A4A");
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
