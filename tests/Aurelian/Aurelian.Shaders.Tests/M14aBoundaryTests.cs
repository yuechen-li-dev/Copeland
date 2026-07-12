using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class M14aBoundaryTests
{
    [Fact]
    public void M14a_DoesNotCreateCopelandVdMirPackage()
    {
        var repoRoot = GetRepoRoot();

        Assert.False(Directory.Exists(Path.Combine(repoRoot, "src", "Copeland.Mir.Vd")));
        Assert.False(Directory.Exists(Path.Combine(repoRoot, "src", "Copeland.Mir.VdMir")));
    }

    [Fact]
    public void M14a_DoesNotCreatePtxBackend()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepoRoot(), "src", "Copeland.Backends.Ptx")));
    }

    [Fact]
    public void M14a_DoesNotCreateSlangBackend()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepoRoot(), "src", "Copeland.Backends.Slang")));
    }

    [Fact]
    public void M14a_DoesNotWireVisibleTriangleToVdMir()
    {
        var sampleRoot = Path.Combine(GetRepoRoot(), "samples", "Aurelian", "Aurelian.VisibleTriangle");
        foreach (var file in Directory.GetFiles(sampleRoot, "*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            Assert.DoesNotContain("VdMir", content, StringComparison.Ordinal);
            Assert.DoesNotContain("VD-MIR", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Visual Direct MIR", content, StringComparison.Ordinal);
        }
    }

    private static string GetRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Copeland.slnx")) &&
                Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "samples")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
