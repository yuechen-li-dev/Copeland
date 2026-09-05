using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Copeland.Profile;
using Copeland.TS.Profiles;

string root = Directory.GetCurrentDirectory();
string samplePath = Path.Combine(root, "samples", "copeland-ts", "profile-pelican-bicycle", "pelican-bicycle.profile.tsx");
string source = File.ReadAllText(samplePath);
string artifactDirectory = Path.Combine(root, "artifacts", "copeland-profile-semantic-geometry-m3");
Directory.CreateDirectory(artifactDirectory);

ProfileCompositionCompilationResult baseline = Compile(source);
File.WriteAllText(Path.Combine(artifactDirectory, "pelican-bicycle.svg"), baseline.Svg!);
File.WriteAllText(Path.Combine(root, "samples", "copeland-ts", "profile-pelican-bicycle", "pelican-bicycle.svg"), baseline.Svg!);

var edits = new (string Name, string Before, string After)[]
{
    ("body-curve-more", "amount: 8.0", "amount: 13.0"),
    ("beak-up", "bulge: 4.0", "bulge: 9.0"),
    ("top-tube-thicker", "TubeFromGuide(bike.topTubeGuide, 8.0)", "TubeFromGuide(bike.topTubeGuide, 12.0)"),
    ("upper-leg-thicker", "TubeFromGuide(bird.upperLegGuide, 10.0)", "TubeFromGuide(bird.upperLegGuide, 7.0)"),
};

var variants = new List<object>();
foreach ((string name, string before, string after) in edits)
{
    string edited = source.Replace(before, after, StringComparison.Ordinal);
    if (edited == source)
    {
        throw new InvalidOperationException($"Edit '{name}' did not match its semantic parameter.");
    }
    ProfileCompositionCompilationResult result = Compile(edited);
    string svgPath = Path.Combine(artifactDirectory, name + ".svg");
    File.WriteAllText(svgPath, result.Svg!);
    variants.Add(new
    {
        name,
        changedLines = source.Split('\n').Zip(edited.Split('\n')).Count(pair => pair.First != pair.Second),
        result.CompositionHash,
        result.CanonicalGeometryHash,
        svgHash = Hash(result.Svg!),
    });
}

string m1SourcePath = Path.Combine(root, "artifacts", "copeland-profile-llm-vector-composition-m0", "baseline", "pelican-bicycle.profile.tsx");
string m1Source = File.Exists(m1SourcePath) ? File.ReadAllText(m1SourcePath) : source;
var manifest = new
{
    milestone = "COPELAND-PROFILE-SEMANTIC-GEOMETRY-M3",
    kind = "concept-geometry-profile-delta-semantic-curves",
    conceptGeometryAdded = true,
    conceptGeometryErased = !baseline.Svg!.Contains("Concept", StringComparison.Ordinal),
    conceptCount = Regex.Matches(source, @"Concept(Point|Path)").Count,
    finalContourContribution = 0,
    segmentLevelProfileDeltaAdded = true,
    replaceSegmentAdded = true,
    semanticCurvesAdded = true,
    explicitSplineIsEscapeHatch = true,
    closednessPreservedByConstruction = true,
    slotAdded = true,
    capsuleAdded = true,
    regularPolygonAdded = true,
    polygonAdded = true,
    customFeatureFunctionsSupported = true,
    customFeatureTemplatesSupported = true,
    runtimeGeometryInterpreterAdded = false,
    fullCadConstraintSolverAdded = false,
    rendererChanged = false,
    sourceComparison = new
    {
        m1Lines = m1Source.Split('\n').Length,
        m3Lines = source.Split('\n').Length,
        m1PolygonCalls = Regex.Matches(m1Source, @"Polygon\(").Count,
        m3PolygonCalls = Regex.Matches(source, @"Polygon\(").Count,
        m1RawControlPoints = Regex.Matches(m1Source, @"control[12]").Count,
        m3RawControlPoints = Regex.Matches(source, @"control[12]").Count,
        m3NamedConcepts = Regex.Matches(source, @"Concept(Point|Path)").Count,
        m3SemanticCurveOperations = Regex.Matches(source, @"(Arc|Bulge)\(").Count,
    },
    baseline.CompositionHash,
    baseline.CanonicalGeometryHash,
    svgHash = Hash(baseline.Svg!),
    variants,
};

string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(artifactDirectory, "manifest.json"), json);
Console.WriteLine(json);

static ProfileCompositionCompilationResult Compile(string source)
{
    ProfileCompositionCompilationResult first = ProfileTsxCompiler.CompileComposition(source, "pelican-bicycle.profile.tsx");
    if (!first.Success)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, first.Diagnostics.Select(item => $"{item.Id}: {item.Message}")));
    }
    ProfileCompositionCompilationResult second = ProfileTsxCompiler.CompileComposition(source, "pelican-bicycle.profile.tsx");
    if (first.CompositionHash != second.CompositionHash || first.CanonicalGeometryHash != second.CanonicalGeometryHash || first.Svg != second.Svg)
    {
        throw new InvalidOperationException("Profile composition was not deterministic.");
    }
    return first;
}

static string Hash(string value)
    => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
