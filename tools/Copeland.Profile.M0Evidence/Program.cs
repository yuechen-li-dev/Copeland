using System.Text.Json;
using Copeland.Profile;
using Machina.VectorAssets;

string root = FindRepositoryRoot();
string output = Path.Combine(root, "artifacts", "copeland-profile-tsx-m0");
Directory.CreateDirectory(output);

var fixtures = new Dictionary<string, ProfileDefinition>(StringComparer.Ordinal)
{
    ["Gear"] = ProfileFixtures.Gear(),
    ["TabbedBadge"] = ProfileFixtures.TabbedBadge(),
    ["Shield"] = ProfileFixtures.Shield(),
    ["MultiHole"] = ProfileFixtures.MultiHole(),
};

List<object> evidence = [];
foreach ((string name, ProfileDefinition definition) in fixtures)
{
    ProfileCompilationResult profile = ProfileCompiler.Compile(definition);
    Require(profile.Success, name + ": " + string.Join("; ", profile.Diagnostics.Select(item => item.Message)));
    string svgPath = Path.Combine(output, name + ".svg");
    File.WriteAllText(svgPath, profile.Svg!);

    object? msdf = null;
    if (name is "Gear" or "TabbedBadge")
    {
        VectorIconCompilationResult icon = ProfileVectorIconCompiler.Compile(
            profile,
            CanonicalSource(name),
            name + ".profile.tsx");
        Require(icon.Success, name + ": " + string.Join("; ", icon.Diagnostics.Select(item => item.Reason)));
        msdf = new
        {
            identity = icon.Artifact!.Identity.Value,
            icon.Artifact.FieldHash,
            icon.Artifact.Width,
            icon.Artifact.Height,
        };
    }

    evidence.Add(new
    {
        name,
        profile.ProfileIrHash,
        profile.CanonicalContourHash,
        contours = profile.Shape!.Contours.Count,
        bounds = new
        {
            profile.Shape.Bounds.MinX,
            profile.Shape.Bounds.MinY,
            profile.Shape.Bounds.MaxX,
            profile.Shape.Bounds.MaxY,
        },
        states = profile.States,
        svg = Path.GetFileName(svgPath),
        msdf,
    });
}

ProfileCompilationResult original = ProfileCompiler.Compile(ProfileFixtures.Gear(8, 8));
ProfileCompilationResult edited = ProfileCompiler.Compile(ProfileFixtures.Gear(12, 12));
var manifest = new
{
    milestone = "COPELAND-PROFILE-TSX-M0",
    kind = "semantic-2d-profile-construction",
    firmamentAudited = true,
    tsxOptional = true,
    tsxRuntimeAdded = false,
    reactDependencyAdded = false,
    geometricSsaUsed = true,
    immutableProfileStates = true,
    canonicalContourIrUsed = true,
    svgIsBackendOnly = true,
    m5MsdfPathReused = true,
    rawSvgAuthoringRequired = false,
    fullCadSystemAdded = false,
    fullSvgRuntimeAdded = false,
    llmEdit = new
    {
        changedParameters = new[] { "count: 8 -> 12", "hole radius: 8 -> 12" },
        originalProfileIrHash = original.ProfileIrHash,
        editedProfileIrHash = edited.ProfileIrHash,
        originalContourHash = original.CanonicalContourHash,
        editedContourHash = edited.CanonicalContourHash,
    },
    fixtures = evidence,
};

string manifestPath = Path.Combine(output, "manifest.json");
File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(manifestPath);

static string CanonicalSource(string name)
{
    return name == "Gear"
        ? "Circle(radius:32) -> RepeatRadial(count:12,toothDepth:8) -> Hole(radius:12)"
        : "RoundedRectangle(width:100,height:56,radius:8) -> Tab(Top) -> Notch(Right) -> Hole";
}

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "JointTaskForce.slnx")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
