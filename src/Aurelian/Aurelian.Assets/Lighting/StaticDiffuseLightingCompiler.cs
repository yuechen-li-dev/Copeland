using System.Diagnostics;
using System.Text.Json;
using Aurelian.Rendering.Contracts.Lighting;

namespace Aurelian.Assets.Lighting;

/// <summary>Explicit optional authoring adapter; never called by runtime asset loading.</summary>
public static class StaticDiffuseLightingCompiler
{
    private sealed record AuthoringDocument(string SceneKey, StaticDiffuseLightingRecipe Recipe);
    public static string Build(StaticDiffuseLightingRecipe recipe, string output, string blenderExecutable, bool reuseReference = true)
    {
        recipe.Validate();
        if (!File.Exists(blenderExecutable)) throw new FileNotFoundException("Installed Blender executable is required for authoring.", blenderExecutable);
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        string scripts = Path.Combine(output, "authoring");
        Directory.CreateDirectory(scripts);
        var assembly = typeof(StaticDiffuseLightingCompiler).Assembly;
        const string resourcePrefix = "Aurelian.Lighting.Authoring.";
        foreach (string resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal)))
        {
            using var source = assembly.GetManifestResourceStream(resource)!;
            using var destination = File.Create(Path.Combine(scripts, resource[resourcePrefix.Length..]));
            source.CopyTo(destination);
        }
        string input = Path.Combine(output, "recipe.json");
        File.WriteAllText(input, JsonSerializer.Serialize(new AuthoringDocument(recipe.ContentKey, recipe),
            new JsonSerializerOptions { WriteIndented = true, IgnoreReadOnlyProperties = true }));
        var start = new ProcessStartInfo(blenderExecutable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in new[] { "--background", "--python", Path.Combine(scripts, "compile_static_diffuse.py"),
                     "--", "--recipe", input, "--output", output })
            start.ArgumentList.Add(argument);
        if (reuseReference) start.ArgumentList.Add("--reuse");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Blender authoring process could not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        File.WriteAllText(Path.Combine(output, "blender.log"), stdout.Result + stderr.Result);
        if (process.ExitCode != 0 || !stdout.Result.Contains("AURELIAN_STATIC_DIFFUSE_ASSET_COMPILED", StringComparison.Ordinal))
            throw new InvalidDataException("Lighting compilation failed. See " + Path.Combine(output, "blender.log"));
        string artifact = Path.Combine(output, "lighting.alight");
        StaticDiffuseLightingAsset.Load(artifact, recipe.ContentKey);
        return artifact;
    }
}
