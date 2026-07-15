using System.Security.Cryptography;
using System.Diagnostics;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class JavaScriptSymbolicCorpusTests
{
    [Theory]
    [InlineData("tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/main-returns-42.ts", 148, "5D8B155F9019A9C94DA044E829D27D02CE86CD8FF0CE96A5B34E1E47BB6E9784")]
    [InlineData("tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/payload-enum-match.ts", 3752, "739F0E3CBB2E470D9D378D846A7FC94CD89EA6AD55EC200764CBE763E49FB384")]
    [InlineData("tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/result-propagation.ts", 1524, "C985DCBA3E110DE6FF22B99934E37A46506CF18A944B41FEF8D3E45F66D01E39")]
    [InlineData("tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/try-except-success.ts", 3434, "0C772ECBE8D6F6A023098494B85A9380840AA3C4E9944110CBE49C431E7A0D7B")]
    [InlineData("tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/record-basic.ts", 2439, "08A8660EDFA7EA4DBD1B4C311F003B6851E83ED6E9B3DE0CA46AF5159E684B3A")]
    [InlineData("tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/record-order-with.ts", 1654, "302B57990326174B71C70BA29A9F740DE50241C12A1E31A0EF9991E0A7D6DBFA")]
    [InlineData("tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/inferred-reuse.ts", 1819, "75116BFD2227A9F84C271F1D18D3849109EB3C2B13A1BB29E71EC69874FE737B")]
    [InlineData("tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/m2-table-basic.ts", 15461, "80AF3FD5ED71D4B9CFCCCDE62877027480255C3E9F841A94C3B77FD9FE46AE5A")]
    [InlineData("tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/record/main.ts", 12156, "C227F69DF91785B71786C7CA5AEB406EA031323AD8ACF22E3F5ABAEA1507B79B")]
    [InlineData("tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/arrays/main.ts", 20419, "0C0740A327B4A80B81118A9B7884902B1EC78277A90F641B9CA0EF439BF0D591")]
    [InlineData("tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/tables-m2/main.ts", 47122, "ACEC71ADB5E76FA85939EEA5789B5EA65543EEBB97D9A9AC55C5ABC8063A89A9")]
    [InlineData("tests/Copeland/Copeland.TS.Tests/TsonTableAssets/Corpus/representative/main.ts", 27074, "2B853C15B5628F1DF81E6130EE28F2A2B3E86A443C537568E3AB9FF16DE62C66")]
    public void Symbolic_corpus_has_stable_bytes_and_hashes(string relativeSourcePath, int expectedByteLength, string expectedSha256)
    {
        string repoRoot = GetRepoRoot();
        string sourcePath = Path.Combine(repoRoot, relativeSourcePath.Replace('/', Path.DirectorySeparatorChar));
        string sourceText = File.ReadAllText(sourcePath);

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            sourceText,
            new CopelandCompilationOptions
            {
                SourcePath = sourcePath,
                ProjectRoot = Path.GetDirectoryName(sourcePath),
                AssetSource = LocalFileAssetSource.Instance,
            });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        Assert.NotNull(compilation.MirCompilation);
        var mirCompilation = compilation.MirCompilation!;
        Assert.NotNull(mirCompilation.Program);

        JavaScriptCompilation emitted = JavaScriptBackend.Emit(
            mirCompilation.Program!,
            new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic });

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.NotNull(emitted.SourceText);
        Assert.DoesNotContain("\r", emitted.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("__cope_m3_", emitted.SourceText, StringComparison.Ordinal);
        Assert.EndsWith("\n", emitted.SourceText, StringComparison.Ordinal);
        string expectedPath = Path.ChangeExtension(sourcePath, ".sym.js");
        Assert.True(File.Exists(expectedPath), $"Missing Symbolic corpus artifact '{expectedPath}'.");
        Assert.Equal(File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal), emitted.SourceText);
        AssertNodeParses(expectedPath);

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(emitted.SourceText);
        Assert.Equal(expectedByteLength, bytes.Length);
        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
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

    private sealed class LocalFileAssetSource : ICopelandAssetSource
    {
        public static LocalFileAssetSource Instance { get; } = new();

        public bool TryRead(string normalizedPath, out string? sourceText)
        {
            if (File.Exists(normalizedPath))
            {
                sourceText = File.ReadAllText(normalizedPath);
                return true;
            }

            sourceText = null;
            return false;
        }
    }

    private static void AssertNodeParses(string filePath)
    {
        using var process = Process.Start(new ProcessStartInfo("node", $"--check \"{filePath}\"")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(process);
        process.WaitForExit();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, stderr);
    }
}
