using System.Diagnostics;
using System.Text.Json;
using Aurelian.Assets.Models;

if (args.Length != 3) throw new ArgumentException("Expected delivery manifest, asset ID, evidence output.");
var model = ModelAssetCatalog.Load(Path.GetFullPath(args[0])).Model(args[1]).Current;
if (!model.Occurrences.Any(occurrence => occurrence.Primitive.Lods.Length > 0))
    throw new InvalidDataException("Delivery specimen needs prepared LODs.");
string[] files = Directory.GetFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories)
    .Where(path => Path.GetFileName(path).Contains("meshopt", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(path).Contains("AssetCompiler", StringComparison.OrdinalIgnoreCase)).ToArray();
string[] dependencies = Directory.GetFiles(AppContext.BaseDirectory, "*.deps.json")
    .Where(path => File.ReadAllText(path).Contains("meshoptimizer", StringComparison.OrdinalIgnoreCase)
        || File.ReadAllText(path).Contains("Aurelian.AssetCompiler", StringComparison.Ordinal)).ToArray();
string[] modules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
    .Select(module => module.ModuleName).Where(name => name.Contains("meshopt", StringComparison.OrdinalIgnoreCase)).ToArray();
if (files.Length != 0 || dependencies.Length != 0 || modules.Length != 0)
    throw new InvalidDataException("Delivery runtime includes a native LOD compiler dependency.");
File.WriteAllText(Path.GetFullPath(args[2]), JsonSerializer.Serialize(new
{
    Accepted = true, model.ContentIdentity, CompilerFiles = files, CompilerDependencies = dependencies, LoadedCompilerModules = modules,
    Levels = model.Occurrences.Select(occurrence => new { occurrence.Id,
        Triangles = occurrence.Primitive.Lods.Select(level => level.Indices.Length / 3) }),
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("AURELIAN_MANAGED_LOD_DELIVERY_PASSED");
