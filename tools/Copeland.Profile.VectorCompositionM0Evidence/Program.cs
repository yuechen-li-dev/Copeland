using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Copeland.Profile;
using Copeland.TS.Profiles;

string root = Directory.GetCurrentDirectory();
string sample = Path.Combine(root, "samples", "copeland-ts", "profile-pelican-bicycle");
string output = Path.Combine(root, "artifacts", "copeland-profile-llm-vector-composition-m0");
Directory.CreateDirectory(output);
string original = File.ReadAllText(Path.Combine(sample, "pelican-bicycle.profile.tsx"));
string[] layerNames = ["RearWheel", "FrontWheel", "RearFrame", "FrontFrame", "Fork", "Seat", "Handlebar",
    "Crank", "CrankArm", "UpperLeg", "LowerLeg", "WebbedFoot", "Tail", "Neck", "BodyAndWing", "Beak", "HeadAndEye"];
var edits = new (string Name, string Before, string After)[]
{
    ("baseline", "", ""),
    ("beak-plus-20-percent", "BeakLength: number = 98.0", "BeakLength: number = 117.6"),
    ("wheels-plus-15-percent", "WheelRadius: number = 52.0", "WheelRadius: number = 59.8"),
    ("body-raised", "BodyLift: number = 44.0", "BodyLift: number = 52.0"),
    ("head-up-wing-larger", "HeadTilt: number = 0.0", "HeadTilt: number = 12.0")
};
var evidence = new List<object>();
List<ProfileSvgLayer>? baselineLayers = null;
foreach (var edit in edits)
{
    string source = edit.Name == "baseline" ? original : original.Replace(edit.Before, edit.After, StringComparison.Ordinal);
    if (edit.Name == "head-up-wing-larger")
    {
        source = source.Replace("WingScale: number = 1.0", "WingScale: number = 1.15", StringComparison.Ordinal);
    }
    string directory = Path.Combine(output, edit.Name);
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "pelican-bicycle.profile.tsx"), source);
    var layers = new List<ProfileSvgLayer>();
    var layerEvidence = new List<object>();
    var profileHashes = new List<string>();
    var contourHashes = new List<string>();
    for (int index = 0; index < layerNames.Length; index++)
    {
        string layerSource = source.Replace("const Layer: int = 0;", $"const Layer: int = {index};", StringComparison.Ordinal);
        ProfileCompilationResult result = Compile(layerSource);
        ProfileCompilationResult repeated = Compile(layerSource);
        Require(result.ProfileIrHash == repeated.ProfileIrHash, "Profile hash changed on repeated compilation.");
        Require(result.CanonicalContourHash == repeated.CanonicalContourHash, "Contour hash changed on repeated compilation.");
        Require(result.Svg == repeated.Svg, "SVG changed on repeated compilation.");
        foreach (VectorContour contour in result.Shape!.Contours)
        {
            for (int segmentIndex = 0; segmentIndex < contour.Segments.Count; segmentIndex++)
            {
                VectorSegment segment = contour.Segments[segmentIndex];
                VectorSegment next = contour.Segments[(segmentIndex + 1) % contour.Segments.Count];
                Require(End(segment) == Start(next), "Contour is not continuous and closed.");
            }
        }
        layers.Add(new ProfileSvgLayer(layerNames[index], result.Shape, result.Style));
        profileHashes.Add(result.ProfileIrHash!);
        contourHashes.Add(result.CanonicalContourHash!);
        File.WriteAllText(Path.Combine(directory, layerNames[index] + ".svg"), result.Svg!);
        layerEvidence.Add(new { name = layerNames[index], result.ProfileIrHash, result.CanonicalContourHash,
            svgHash = Hash(result.Svg!), contours = result.Shape.Contours.Count,
            segments = result.Shape.Contours.Sum(contour => contour.Segments.Count), style = result.Style,
            states = result.States.Select(state => new { state.Name, state.OperationKind, state.ContourHash }) });
    }
    string svg = ProfileSvgExporter.ExportLayers(layers);
    Require(svg == ProfileSvgExporter.ExportLayers(layers), "Layer export is not deterministic.");
    if (edit.Name == "baseline")
    {
        baselineLayers = layers;
    }
    else
    {
        string[] expectedChanges = edit.Name switch
        {
            "beak-plus-20-percent" => ["Beak"],
            "wheels-plus-15-percent" => layerNames,
            "body-raised" => ["UpperLeg", "LowerLeg", "Tail", "Neck", "BodyAndWing", "Beak", "HeadAndEye"],
            _ => ["BodyAndWing", "Beak", "HeadAndEye"]
        };
        string[] actualChanges = layers.Where((layer, index) =>
            layer.Shape.NormalizedGeometryHash != baselineLayers![index].Shape.NormalizedGeometryHash)
            .Select(layer => layer.Name).ToArray();
        Require(expectedChanges.SequenceEqual(actualChanges), "Edit changed unexpected component geometry.");
        if (edit.Name == "beak-plus-20-percent")
        {
            Require(Math.Abs(layers[15].Shape.Bounds.Width / baselineLayers![15].Shape.Bounds.Width - 1.2) < 1e-9,
                "Beak did not become exactly 20 percent longer.");
        }
        if (edit.Name == "wheels-plus-15-percent")
        {
            Require(Math.Abs(layers[0].Shape.Bounds.Width / baselineLayers![0].Shape.Bounds.Width - 1.15) < 1e-9,
                "Wheel did not become exactly 15 percent larger.");
        }
        if (edit.Name == "body-raised")
        {
            Require(Math.Abs(layers[14].Shape.Bounds.MinY - baselineLayers![14].Shape.Bounds.MinY - 8) < 1e-9,
                "Body did not rise by eight logical units.");
        }
    }
    File.WriteAllText(Path.Combine(directory, "pelican-bicycle.svg"), svg);
    if (edit.Name == "baseline")
    {
        File.WriteAllText(Path.Combine(sample, "pelican-bicycle.svg"), svg);
    }
    string[] beforeLines = original.Split('\n');
    string[] afterLines = source.Split('\n');
    var differences = new List<string>();
    for (int index = 0; index < beforeLines.Length; index++)
    {
        if (beforeLines[index] != afterLines[index])
        {
            differences.Add($"@@ line {index + 1} @@\n-{beforeLines[index]}\n+{afterLines[index]}");
        }
    }
    File.WriteAllText(Path.Combine(directory, "semantic-edit.diff"), string.Join("\n", differences));
    string pathData = string.Join(" ", XElement.Parse(svg).Elements().Select(path => path.Attribute("d")!.Value));
    MatchCollection polygons = Regex.Matches(source, @"Polygon\(\{ points: \[(.*?)\] \}\)", RegexOptions.Singleline);
    evidence.Add(new { variant = edit.Name, beforeSourceHash = Hash(original), afterSourceHash = Hash(source),
        profileHash = Hash(string.Join("\n", profileHashes)), contourHash = Hash(string.Join("\n", contourHashes)),
        svgHash = Hash(svg), changedLines = differences.Count, diff = differences,
        sourceLineCount = source.Split('\n').Length,
        sourceNumericLiteralCount = Regex.Matches(source, @"\b\d+(?:\.\d+)?\b").Count,
        exportedNumericTokenCount = Regex.Matches(svg, @"-?\d+(?:\.\d+)?(?:E[+-]?\d+)?").Count,
        exportedPathCoordinateCount = Regex.Matches(pathData, @"-?\d+(?:\.\d+)?(?:E[+-]?\d+)?").Count,
        exportedPathCommandCount = Regex.Matches(pathData, @"[MLQCZ]").Count,
        ordinaryHelperCount = Regex.Matches(source, @"^function ", RegexOptions.Multiline).Count,
        polygonEscapeSites = polygons.Count,
        polygonNumericLiteralCount = polygons.Sum(polygon => Regex.Matches(polygon.Groups[1].Value, @"\b\d+(?:\.\d+)?\b").Count),
        layerCount = layers.Count, layerEvidence });
    Console.WriteLine($"{edit.Name}: {layers.Count} layers, SVG {Hash(svg)}, {differences.Count} changed lines");
}
var manifest = new
{
    milestone = "COPELAND-PROFILE-LLM-VECTOR-COMPOSITION-M0",
    benchmark = "pelican-riding-bicycle",
    outcome = "B",
    semanticProfileUsed = true,
    targetEffort = "10-15-minute-sketch",
    rawSvgAuthoredDirectly = false,
    tsxCompositionUsed = true,
    typedLayoutUsed = true,
    layeredConstructionUsed = true,
    typedStyleRecordsWithExpressions = true,
    rendererChanged = false,
    noRuntimeTsx = true,
    deterministicRepeatedCompilation = true,
    allContoursContinuousAndClosed = true,
    sceneHashLaw = "SHA256 of newline-joined layer hashes in paint order; not a Boolean-union contour hash",
    variants = evidence
};
string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(output, "manifest.json"), json);
File.WriteAllText(Path.Combine(sample, "evidence.json"), json);

static ProfileCompilationResult Compile(string source)
{
    Require(!source.Contains("<svg", StringComparison.Ordinal) && !source.Contains("<path", StringComparison.Ordinal), "Raw SVG in source.");
    ProfileCompilationResult result = ProfileTsxCompiler.Compile(source, "pelican-bicycle.profile.tsx");
    Require(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}")));
    return result;
}

static VectorPoint Start(VectorSegment segment) => segment switch
{
    VectorLine line => line.P0,
    VectorQuadratic quadratic => quadratic.P0,
    VectorCubic cubic => cubic.P0,
    _ => throw new InvalidOperationException("Unknown segment.")
};

static VectorPoint End(VectorSegment segment) => segment switch
{
    VectorLine line => line.P1,
    VectorQuadratic quadratic => quadratic.P2,
    VectorCubic cubic => cubic.P3,
    _ => throw new InvalidOperationException("Unknown segment.")
};

static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
