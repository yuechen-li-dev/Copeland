using System.Security.Cryptography;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Games;

/// <summary>Explicit built-in assets and compiled-program cache; no repository paths at runtime.</summary>
public sealed class GameAssets
{
    private readonly Dictionary<string, CompiledGraphicsProgram> programs = new(StringComparer.Ordinal);

    public CompiledGraphicsProgram Shader(string name)
    {
        if (programs.TryGetValue(name, out CompiledGraphicsProgram? cached))
        {
            return cached;
        }
        using Stream stream = Open(name);
        using var reader = new StreamReader(stream);
        var module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(name, reader.ReadToEnd())]));
        if (!module.Success)
        {
            throw new InvalidOperationException(string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        }
        CompiledGraphicsProgram program = CompiledGraphicsProgramExporter.Export(module, VdMirGraphicsBackend.Compile(module));
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
