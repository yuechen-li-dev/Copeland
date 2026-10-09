using Copeland.TS.Compiler;
using Copeland.TS.Gpu.VdMir;

namespace Copeland.TS.Gpu;

/// <summary>Loads an immutable source snapshot through a caller-owned asset reader.</summary>
public static class GpuSourceLoader
{
    public static IReadOnlyList<GpuSourceFile> Load(string rootPath, Func<string, string?> readSource)
    {
        ArgumentNullException.ThrowIfNull(readSource);
        var sources = new Dictionary<string, GpuSourceFile>(StringComparer.Ordinal);
        LoadSource(GpuModuleGraph.Normalize(rootPath), required: true);
        return sources.Values.OrderBy(source => source.Path, StringComparer.Ordinal).ToArray();

        void LoadSource(string path, bool required)
        {
            if (sources.ContainsKey(path))
            {
                return;
            }
            string? text = readSource(path);
            if (text is null)
            {
                if (required)
                {
                    throw new FileNotFoundException("Root shader source is unavailable.", path);
                }
                return;
            }
            sources.Add(path, new(path, text));
            var projectSource = new CopelandProjectSource(path, path, text);
            foreach (var import in CopelandProjectCompiler.ReadSourceImports(projectSource))
            {
                foreach (string candidate in GpuModuleGraph.ImportCandidates(path, import.Specifier))
                {
                    LoadSource(candidate, required: false);
                }
            }
        }
    }
}
