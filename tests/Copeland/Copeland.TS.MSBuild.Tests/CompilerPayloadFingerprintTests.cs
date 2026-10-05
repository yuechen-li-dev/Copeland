using System.Reflection;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Copeland.TS.MSBuild.Tests;

public sealed class CompilerPayloadFingerprintTests
{
    [Fact]
    public void Equal_version_size_and_timestamp_do_not_hide_repacked_compiler_bytes()
    {
        string directory = Path.Combine(Path.GetTempPath(), "copeland-fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "first.dll");
            string second = Path.Combine(directory, "second.dll");
            WriteAssembly(first, "A");
            WriteAssembly(second, "B");
            DateTime timestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(first, timestamp);
            File.SetLastWriteTimeUtc(second, timestamp);
            Assert.Equal(new FileInfo(first).Length, new FileInfo(second).Length);
            Assert.Equal(File.GetLastWriteTimeUtc(first), File.GetLastWriteTimeUtc(second));

            Assert.Equal(AssemblyName.GetAssemblyName(first).FullName, AssemblyName.GetAssemblyName(second).FullName);
            Assert.NotEqual(Fingerprint(first), Fingerprint(second));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Fingerprint(string path)
    {
        MethodInfo method = typeof(CopelandCompile).GetMethod("AppendCompilerPayloadIdentityAndBytes", BindingFlags.NonPublic | BindingFlags.Static)!;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        method.Invoke(null, [hash, AssemblyName.GetAssemblyName(path), path]);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void WriteAssembly(string path, string value)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(file => MetadataReference.CreateFromFile(file));
        var tree = CSharpSyntaxTree.ParseText("public static class Repacked { public static string Value() => \"" + value + "\"; }");
        var compilation = CSharpCompilation.Create("RepackedCompiler", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));
        using var stream = File.Create(path);
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }
}
