using System.Security.Cryptography;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Games;

/// <summary>Explicit built-in assets and compiled-program cache; no repository paths at runtime.</summary>
public sealed class GameAssets
{
    private readonly Dictionary<string, CompiledGraphicsProgram> programs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> computePrograms = new(StringComparer.Ordinal);

    public byte[] ComputeShader(string name)
    {
        if (computePrograms.TryGetValue(name, out var cached))
        {
            return cached;
        }
        var module = GpuComputeBinder.Compile(new(GpuSourceLoader.Load(name, ReadShaderSource)));
        if (!module.Success)
        {
            throw new InvalidDataException(string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        }
        var compiled = VdMirComputeBackend.Compile(module);
        if (!compiled.SpirvValidated || compiled.Spirv.Length == 0)
        {
            throw new InvalidDataException(compiled.DxcOutput + compiled.SpirvValidationOutput);
        }
        computePrograms.Add(name, compiled.Spirv);
        return compiled.Spirv;
    }

    public CompiledGraphicsProgram Shader(string name)
    {
        if (programs.TryGetValue(name, out CompiledGraphicsProgram? cached))
        {
            return cached;
        }
        IReadOnlyList<GpuSourceFile> sources = GpuSourceLoader.Load(name, ReadShaderSource);
        var module = GpuGraphicsBinder.Compile(new GpuCompilationRequest(sources));
        if (!module.Success)
        {
            throw new InvalidOperationException(string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        }
        // Core graphics needs no optional Vulkan 1.3 demote feature; MASK lowers to the core discard instruction.
        var backend = VdMirGraphicsBackend.Compile(module, targetEnvironment: "vulkan1.2");
        if (!backend.Vertex.SpirvValidated || !backend.Pixel.SpirvValidated)
        {
            throw new InvalidOperationException($"Shader '{name}' failed validation: "
                + backend.Vertex.DxcOutput + backend.Vertex.SpirvValidationOutput
                + backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
        }
        CompiledGraphicsProgram program = CompiledGraphicsProgramExporter.Export(module, backend);
        programs.Add(name, program);
        return program;
    }

    public string FontDirectory()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aurelian", "assets");
        foreach (string name in new[] { "CrimsonText-Regular.ttf", "SpaceMono-Regular.ttf" })
        {
            using Stream stream = Open(name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            // Both fonts share a versioned directory; hashes verify cached bytes before reuse.
            string directory = Path.Combine(root, "starter-v1");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name);
            if (!File.Exists(path) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) != hash)
            {
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, path, overwrite: true);
            }
        }
        return Path.Combine(root, "starter-v1");
    }

    private static Stream Open(string name) => typeof(GameAssets).Assembly.GetManifestResourceStream("Aurelian.Games." + name)
        ?? throw new FileNotFoundException($"Built-in game asset '{name}' is unavailable.");

    private static string? ReadShaderSource(string name)
    {
        using Stream? stream = typeof(GameAssets).Assembly.GetManifestResourceStream("Aurelian.Games." + name);
        if (stream is null)
        {
            return null;
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
