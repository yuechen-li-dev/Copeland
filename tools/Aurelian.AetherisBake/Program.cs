using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Aetheris;
using Aetheris.Kernel.Firmament.Scene;

if (args.Length < 3 || args[0] != "bake")
{
    Console.Error.WriteLine("Usage: Aurelian.AetherisBake bake source.firmament out.json [--solid-prefix occurrence.path] [--allow-mesh-approximation]");
    return 1;
}
string source = Path.GetFullPath(args[1]);
string output = Path.GetFullPath(args[2]);
if (source.Equals(output, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output must differ from input.");
var prefixes = new List<string>();
bool allowApproximation = false;
for (int index = 3; index < args.Length; index++)
{
    if (args[index] == "--solid-prefix" && index + 1 < args.Length) prefixes.Add(args[++index]);
    else if (args[index] == "--allow-mesh-approximation") allowApproximation = true;
    else throw new ArgumentException("Unknown bake option: " + args[index]);
}
using var session = new FirmamentSceneSession();
var compilation = session.CompileFile(source);
if (!compilation.IsSuccess)
{
    foreach (var diagnostic in compilation.Diagnostics) Console.Error.WriteLine(diagnostic.Code + ": " + diagnostic.Message);
    return 1;
}
CompiledScene scene = compilation.Scene!;
Matrix4x4 conversion = new(0.001f, 0, 0, 0, 0, 0, -0.001f, 0, 0, 0.001f, 0, 0, 0, 0, 0, 1);
var definitions = scene.Display.Definitions.OrderBy(definition => definition.Id, StringComparer.Ordinal).Select(definition =>
{
    string representation = definition.MeshPipeline == "SceneRectangularBoundary" ? "planar-exact" : "triangle-approximation";
    if (definition.Diagnostics.Count != 0 || definition.Indices.Length == 0)
    {
        throw new InvalidDataException("Display-only incomplete geometry cannot be baked for gameplay: " + definition.Identity);
    }
    var faces = new string?[definition.Indices.Length / 3];
    foreach (var range in definition.Ranges ?? [])
    {
        for (int index = range.StartTriangle; index < range.StartTriangle + range.TriangleCount; index++) faces[index] = range.FaceId;
    }
    return new AetherisMeshAsset(definition.Id, definition.Identity, definition.Positions.Select(value => (float)value).ToArray(),
        definition.Normals.Select(value => (float)value).ToArray(), definition.Indices, representation, faces);
}).ToArray();
var occurrences = scene.Display.Occurrences.Where(occurrence => occurrence.DefinitionId is not null)
    .OrderBy(occurrence => occurrence.Path, StringComparer.Ordinal).Select(occurrence =>
{
    bool solid = prefixes.Any(prefix => occurrence.Path == prefix || occurrence.Path.StartsWith(prefix + ".", StringComparison.Ordinal));
    var definition = definitions.Single(definition => definition.Id == occurrence.DefinitionId);
    if (solid && definition.Representation != "planar-exact" && !allowApproximation)
    {
        throw new InvalidDataException("Curved/trimmed collision requires explicit --allow-mesh-approximation: " + occurrence.Path);
    }
    double[] m = occurrence.Transform;
    Matrix4x4 matrix = new((float)m[0], (float)m[1], (float)m[2], (float)m[3], (float)m[4], (float)m[5], (float)m[6], (float)m[7],
        (float)m[8], (float)m[9], (float)m[10], (float)m[11], (float)m[12], (float)m[13], (float)m[14], (float)m[15]);
    matrix *= conversion;
    float[] frame = [matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24,
        matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44];
    float[] color = [0.5f, 0.6f, 0.65f, 1];
    if (scene.Appearances.TryGetValue(occurrence.Id, out var look))
    {
        color = [(float)look.Preview.Red, (float)look.Preview.Green, (float)look.Preview.Blue, (float)look.Preview.Opacity];
    }
    return new AetherisOccurrenceAsset(occurrence.Path, occurrence.DefinitionId!, frame, color, solid);
}).ToArray();
foreach (string prefix in prefixes)
{
    if (!occurrences.Any(occurrence => occurrence.Path == prefix || occurrence.Path.StartsWith(prefix + ".", StringComparison.Ordinal)))
        throw new ArgumentException("Collision prefix selected no geometry: " + prefix);
}
var asset = new AetherisSceneAsset(AetherisSceneAsset.CurrentSchema, Path.GetFileName(source),
    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))), definitions, occurrences);
asset.Compose();
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
string json = JsonSerializer.Serialize(asset, AetherisAssetJsonContext.Default.AetherisSceneAsset);
File.WriteAllText(output, json.Replace("\r\n", "\n", StringComparison.Ordinal));
Console.WriteLine($"Baked {definitions.Length} definitions and {occurrences.Length} occurrences; {occurrences.Count(occurrence => occurrence.Solid)} solid; {output}");
return 0;
