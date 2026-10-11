using Aurelian.Shaders.Compute;
using Aurelian.Shaders.Graphics;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class BuiltinShaderLibraryTests
{
    public static IEnumerable<object[]> EntryPoints => BuiltinShaderLibrary.Sources
        .Where(source => source.Kind != ShaderSourceKind.Module)
        .Select(source => new object[] { source.Name, source.Kind });

    [Fact]
    public void CatalogCoversEveryEmbeddedSourceAndCopiesTheSameBytesForProofs()
    {
        const string prefix = "Aurelian.Shaders.Sources.";
        var compilerAssembly = typeof(VdMirGraphicsBackend).Assembly;
        Assert.NotEqual(compilerAssembly, typeof(BuiltinShaderLibrary).Assembly);
        Assert.DoesNotContain(compilerAssembly.GetManifestResourceNames(), name => name.EndsWith(".v.ts", StringComparison.Ordinal));
        var embedded = typeof(BuiltinShaderLibrary).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => name[prefix.Length..]).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(BuiltinShaderLibrary.Sources.Select(source => source.Name).Order(StringComparer.Ordinal), embedded);
        foreach (var source in BuiltinShaderLibrary.Sources)
        {
            string copied = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", source.Name));
            Assert.Equal(copied, BuiltinShaderLibrary.Read(source.Name));
            var snapshot = BuiltinShaderLibrary.LoadSources(source.Name);
            Assert.Contains(snapshot, file => file.Path == source.Name && file.Source == copied);
            Assert.All(snapshot, file => Assert.Equal(BuiltinShaderLibrary.Read(file.Path), file.Source));
        }
        // Imported modules travel with the entry rather than an application-specific embedding list.
        Assert.Contains(BuiltinShaderLibrary.LoadSources("SurfaceResolve3D.v.ts"), source => source.Path == "Lighting3D.v.ts");
        Assert.Contains(BuiltinShaderLibrary.LoadSources("TemporalResolve3D.v.ts"), source => source.Path == "TemporalPolicy.v.ts");
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void EveryCatalogEntryCompilesToValidatedSpirvFromTheOwningAssembly(string name, ShaderSourceKind kind)
    {
        var sources = BuiltinShaderLibrary.LoadSources(name);
        if (kind == ShaderSourceKind.Compute)
        {
            var module = GpuComputeBinder.Compile(new(sources));
            Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Message)));
            var backend = VdMirComputeBackend.Compile(module);
            Assert.True(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
            Assert.NotEmpty(backend.Spirv);
        }
        else
        {
            var module = GpuGraphicsBinder.Compile(new(sources));
            Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Message)));
            var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
            Assert.True(backend.Vertex.SpirvValidated, backend.Vertex.DxcOutput + backend.Vertex.SpirvValidationOutput);
            Assert.True(backend.Pixel.SpirvValidated, backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
            Assert.NotEmpty(backend.Vertex.Spirv);
            Assert.NotEmpty(backend.Pixel.Spirv);
        }
    }

    [Fact]
    public void UnknownSourcesAndWrongPipelineKindsHaveNamedDiagnostics()
    {
        Assert.Null(BuiltinShaderLibrary.TryRead("../Cloth3D.v.ts"));
        Assert.Null(BuiltinShaderLibrary.TryRead("Missing.v.ts"));
        Assert.StartsWith("AUR-SHADER-LIBRARY-001:", Assert.Throws<FileNotFoundException>(() =>
            BuiltinShaderLibrary.LoadSources("Missing.v.ts")).Message);
        Assert.StartsWith("AUR-SHADER-LIBRARY-003:", Assert.Throws<ArgumentException>(() =>
            BuiltinShaderLibrary.RequireKind("Lighting3D.v.ts", ShaderSourceKind.Graphics)).Message);
        Assert.StartsWith("AUR-SHADER-LIBRARY-003:", Assert.Throws<ArgumentException>(() =>
            BuiltinShaderLibrary.RequireKind("Cloth3D.v.ts", ShaderSourceKind.Graphics)).Message);
    }
}
