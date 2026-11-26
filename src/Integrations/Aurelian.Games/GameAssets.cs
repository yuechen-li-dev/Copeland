using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Games;

/// <summary>Explicit built-in assets and compiled-program cache; no repository paths at runtime.</summary>
public sealed class GameAssets
{
    /// <summary>Authoring operation: import HDR, integrate on the supplied GPU and save once.</summary>
    public Aurelian.Rendering.Contracts.Models.EnvironmentLighting CompileEnvironment(
        Aurelian.Graphics.Vulkan.Device.AurelianVulkanPlant plant, string hdrPath, string artifactPath)
    {
        var source = Aurelian.Assets.Lighting.RadianceEnvironmentImporter.Load(hdrPath);
        var environment = Aurelian.Graphics.Vulkan.Native3D.VulkanEnvironmentCompiler.Compile(
            plant, Shader("EnvironmentCompile3D.v.ts"), source.Width, source.Height, source.Rgba.AsSpan());
        Aurelian.Assets.Lighting.EnvironmentLightingAsset.Save(artifactPath, environment);
        return environment;
    }

    public Aurelian.Rendering.Contracts.Models.EnvironmentLighting LoadEnvironment(string path)
    {
        return Aurelian.Assets.Lighting.EnvironmentLightingAsset.Load(path);
    }
    public Aurelian.Rendering.Contracts.Lighting.StaticDiffuseLighting Lighting(string path, string sceneKey) =>
        Aurelian.Assets.Lighting.StaticDiffuseLightingAsset.Load(path, sceneKey);
    private readonly Dictionary<string, CompiledGraphicsProgram> programs = new(StringComparer.Ordinal);
    private readonly BuiltinComputeShaderCache computePrograms = new();
    private readonly string? graphicsPackDirectory;
    private readonly bool bakeGraphicsPack;

    public GameAssets() { }

    /// <summary>Load an explicit graphics shader pack, or populate it from shader sources.</summary>
    public GameAssets(string graphicsPackDirectory, bool bakeGraphicsPack = false)
    {
        this.graphicsPackDirectory = Path.GetFullPath(graphicsPackDirectory);
        this.bakeGraphicsPack = bakeGraphicsPack;
        if (bakeGraphicsPack)
        {
            Directory.CreateDirectory(this.graphicsPackDirectory);
        }
    }

    public byte[] ComputeShader(string name)
    {
        return computePrograms.Get(name);
    }

    public CompiledGraphicsProgram Shader(string name)
    {
        BuiltinShaderLibrary.RequireKind(name, ShaderSourceKind.Graphics);
        if (programs.TryGetValue(name, out CompiledGraphicsProgram? cached))
        {
            return cached;
        }
        string? packPath = graphicsPackDirectory is null ? null : Path.Combine(graphicsPackDirectory, name + ".json");
        if (packPath is not null && !bakeGraphicsPack)
        {
            using FileStream stream = File.OpenRead(packPath);
            var packed = JsonSerializer.Deserialize<CompiledGraphicsProgram>(stream);
            if (packed?.FormatVersion != CompiledGraphicsProgram.CurrentFormatVersion || packed.Shaders?.Stages is not { Count: 2 } stages)
            {
                throw new InvalidDataException($"Unsupported compiled graphics program in '{packPath}'.");
            }
            foreach (CompiledShaderStage stage in stages)
            {
                if (stage.SpirvBytes is not { Length: > 0 } bytes
                    || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), stage.SpirvSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"SPIR-V content hash does not match '{packPath}' ({stage.Stage}).");
                }
            }
            programs.Add(name, packed);
            return packed;
        }
        IReadOnlyList<GpuSourceFile> sources = BuiltinShaderLibrary.LoadSources(name);
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
        if (packPath is not null)
        {
            string temporary = packPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = File.Create(temporary))
                {
                    JsonSerializer.Serialize(stream, program);
                }
                File.Move(temporary, packPath, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
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

}
