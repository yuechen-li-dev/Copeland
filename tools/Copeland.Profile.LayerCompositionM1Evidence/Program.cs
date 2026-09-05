using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Copeland.Profile;
using Copeland.TS.Profiles;

string root = Directory.GetCurrentDirectory();
string sampleDirectory = Path.Combine(root, "samples", "copeland-ts", "profile-pelican-bicycle");
string artifactDirectory = Path.Combine(root, "artifacts", "copeland-profile-layer-composition-m1");
Directory.CreateDirectory(artifactDirectory);
string sourcePath = Path.Combine(sampleDirectory, "pelican-bicycle.profile.tsx");
string source = File.ReadAllText(sourcePath);
string m0SvgPath = Path.Combine(root, "artifacts", "copeland-profile-llm-vector-composition-m0", "baseline", "pelican-bicycle.svg");
string m0Svg = File.ReadAllText(m0SvgPath);

var edits = new (string Name, string Before, string After, string[] ChangedProfiles)[]
{
    ("baseline", "", "", []),
    ("beak-plus-20-percent", "BeakLength: number = 98.0", "BeakLength: number = 117.6", ["Beak"]),
    ("wheels-plus-15-percent", "WheelRadius: number = 52.0", "WheelRadius: number = 59.8", []),
    ("body-raised", "BodyLift: number = 44.0", "BodyLift: number = 52.0", ["UpperLeg", "LowerLeg", "Tail", "Neck", "BodyAndWing", "Beak", "HeadAndEye"]),
    ("head-up-wing-larger", "HeadTilt: number = 0.0", "HeadTilt: number = 12.0", ["BodyAndWing", "Beak", "HeadAndEye"]),
};

ProfileCompositionCompilationResult baseline = Compile(source);
var baselineItems = baseline.Composition!.Layers
    .SelectMany(layer => layer.Items)
    .ToDictionary(item => item.Id, StringComparer.Ordinal);
var variantEvidence = new List<object>();
foreach ((string name, string before, string after, string[] changedProfiles) in edits)
{
    string variantSource = name == "baseline"
        ? source
        : source.Replace(before, after, StringComparison.Ordinal);
    if (name == "head-up-wing-larger")
    {
        variantSource = variantSource.Replace(
            "WingScale: number = 1.0",
            "WingScale: number = 1.15",
            StringComparison.Ordinal);
    }

    ProfileCompositionCompilationResult result = Compile(variantSource);
    string directory = Path.Combine(artifactDirectory, name);
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "pelican-bicycle.profile.tsx"), variantSource);
    File.WriteAllText(Path.Combine(directory, "pelican-bicycle.svg"), result.Svg!);
    string[] actualChanges = result.Composition!.Layers
        .SelectMany(layer => layer.Items)
        .Where(item => item.CanonicalContourHash != baselineItems[item.Id].CanonicalContourHash)
        .Select(item => item.Id)
        .ToArray();
    string[] expectedChanges = name == "wheels-plus-15-percent"
        ? baselineItems.Keys.ToArray()
        : changedProfiles;
    Require(expectedChanges.Order(StringComparer.Ordinal).SequenceEqual(actualChanges.Order(StringComparer.Ordinal)),
        $"{name} changed an unexpected Profile set.");

    string[] originalLines = source.Split('\n');
    string[] variantLines = variantSource.Split('\n');
    int changedLines = originalLines.Zip(variantLines).Count(pair => pair.First != pair.Second);
    Require(changedLines == (name == "head-up-wing-larger" ? 2 : name == "baseline" ? 0 : 1),
        $"{name} did not preserve localized edit line count.");
    variantEvidence.Add(new
    {
        variant = name,
        changedLines,
        changedProfiles = actualChanges,
        result.CompositionHash,
        result.CanonicalGeometryHash,
        svgHash = Hash(result.Svg!),
    });
}

for (int iteration = 0; iteration < 101; iteration++)
{
    ProfileCompositionCompilationResult repeated = Compile(source);
    Require(repeated.CompositionHash == baseline.CompositionHash, "Composition hash changed during burn-in.");
    Require(repeated.CanonicalGeometryHash == baseline.CanonicalGeometryHash, "Geometry hash changed during burn-in.");
    Require(repeated.Svg == baseline.Svg, "SVG changed during burn-in.");
}

string m0PaintHash = PaintHash(m0Svg);
string m1PaintHash = PaintHash(baseline.Svg!);
Require(m0PaintHash == m1PaintHash, "M1 changed canonical path or fill paint data relative to M0.");
File.WriteAllText(Path.Combine(sampleDirectory, "pelican-bicycle.svg"), baseline.Svg!);

var reorderEvidence = new List<object>();
RecordReorder("pelican-body-before-bicycle-frame", ReorderLayers(baseline.Composition!, [0, 3, 1, 2, 4]));
RecordReorder("pelican-details-before-body", ReorderLayers(baseline.Composition!, [0, 1, 2, 4, 3]));
RecordReorder("beak-in-front-of-head", ReorderDetails(baseline.Composition!));

string[] sourceLines = source.Split('\n');
var manifest = new
{
    milestone = "COPELAND-PROFILE-LAYER-COMPOSITION-M1",
    kind = "typed-source-level-vector-painter-composition",
    numericLayerSelectorRequired = false,
    numericLayerSelectorCount = Regex.Matches(source, @"const\s+Layer\s*:\s*int|BuildLayer\s*\(").Count,
    typedLayerIdentity = true,
    sourceOrderDefinesPainterOrder = true,
    semanticLayerCount = baseline.Composition!.Layers.Count,
    semanticLayers = baseline.Composition.Layers.Select(layer => new
    {
        name = layer.Id.Name,
        profiles = layer.Items.Select(item => item.Id),
    }),
    layerRelatedSourceLines = sourceLines.Count(line => line.Contains("Layer", StringComparison.Ordinal)),
    sourceLineCount = sourceLines.Length,
    m0SvgHash = Hash(m0Svg),
    m1SvgHash = Hash(baseline.Svg!),
    canonicalPaintHashBefore = m0PaintHash,
    canonicalPaintHashAfter = m1PaintHash,
    visualPaintDataUnchanged = m0PaintHash == m1PaintHash,
    baseline.CompositionHash,
    baseline.CanonicalGeometryHash,
    deterministicCompilationRuns = 107,
    profileGeometrySemanticsChanged = false,
    profileFeatureIdentityChanged = false,
    svgLayerGroupsEmitted = true,
    runtimeLayerTreeAdded = false,
    reactDependencyAdded = false,
    rendererChanged = false,
    duplicateLayerLaw = "reject exact duplicate semantic identities",
    emptyLayerLaw = "allow in source and erase during composition lowering",
    variants = variantEvidence,
    reorderTests = reorderEvidence,
};
string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(artifactDirectory, "manifest.json"), json);
File.WriteAllText(Path.Combine(sampleDirectory, "layer-composition-evidence.json"), json);
Console.WriteLine($"M1: {baseline.Composition.Layers.Count} layers, 17 profiles, SVG {Hash(baseline.Svg!)}");
Console.WriteLine($"Paint parity with M0: {m0PaintHash}");
Console.WriteLine("Deterministic compilations: 107");

void RecordReorder(string name, ProfileComposition composition)
{
    string svg = ProfileSvgExporter.ExportComposition(composition);
    Require(composition.SemanticHash != baseline.CompositionHash, $"{name} did not change composition hash.");
    Require(composition.CanonicalGeometryHash == baseline.CanonicalGeometryHash, $"{name} changed geometry hash.");
    Require(svg != baseline.Svg, $"{name} did not change SVG painter order.");
    File.WriteAllText(Path.Combine(artifactDirectory, name + ".svg"), svg);
    reorderEvidence.Add(new
    {
        name,
        compositionHash = composition.SemanticHash,
        geometryHash = composition.CanonicalGeometryHash,
        svgHash = Hash(svg),
        layerOrder = composition.Layers.Select(layer => layer.Id.Name),
        detailOrder = composition.Layers
            .Single(layer => layer.Id.Name == "Pelican Details")
            .Items.Select(item => item.Id),
    });
}

static ProfileComposition ReorderLayers(ProfileComposition sourceComposition, int[] indices)
    => new(indices.Select(index => sourceComposition.Layers[index]).ToArray());

static ProfileComposition ReorderDetails(ProfileComposition sourceComposition)
{
    ProfileLayer[] layers = sourceComposition.Layers.ToArray();
    int index = Array.FindIndex(layers, layer => layer.Id.Name == "Pelican Details");
    layers[index] = layers[index] with { Items = layers[index].Items.Reverse().ToArray() };
    return new ProfileComposition(layers);
}

static ProfileCompositionCompilationResult Compile(string text)
{
    ProfileCompositionCompilationResult result = ProfileTsxCompiler.CompileComposition(text, "pelican-bicycle.profile.tsx");
    Require(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}")));
    return result;
}

static string PaintHash(string svg)
{
    XElement root = XElement.Parse(svg);
    string canonical = string.Join("\n", root.Descendants()
        .Where(element => element.Name.LocalName == "path")
        .Select(path => path.Attribute("fill")!.Value + "\n" + path.Attribute("d")!.Value));
    return Hash(canonical);
}

static string Hash(string value)
    => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
