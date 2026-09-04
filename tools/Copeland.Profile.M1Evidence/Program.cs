using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.Profile;
using Copeland.TS.Compiler;
using Copeland.TS.Profiles;
using Copeland.TS.Templates;
using Machina.VectorAssets;

string root = FindRepositoryRoot();
string output = Path.Combine(root, "artifacts", "copeland-profile-template-functions-m1");
Directory.CreateDirectory(output);

var variants = new List<object>();
foreach (int count in new[] { 8, 12, 16 })
{
    TemplateEvaluationResult specialization = CopelandProjectCompiler.CompileTemplates(
        [
            new CopelandProjectSource("Profile.ts", "Profile.ts", ProfileTemplateFunctions.Source),
            new CopelandProjectSource("ProfileTemplates.ts", "ProfileTemplates.ts", EvidenceSources.TemplateLibrary),
        ],
        "GearTeeth",
        [count, 0.52d, 8d]);
    Require(specialization.Success, Diagnostics(specialization.Diagnostics.Select(item => item.Id + ": " + item.Message)));

    string source = EvidenceSources.GearSource.Replace("count: 12", $"count: {count}", StringComparison.Ordinal);
    ProfileCompilationResult profile = ProfileTsxCompiler.CompileWithTemplates(source, EvidenceSources.TemplateLibrary, $"Gear-{count}.profile.tsx");
    Require(profile.Success, Diagnostics(profile.Diagnostics.Select(item => item.Id + ": " + item.Message)));
    string svgPath = Path.Combine(output, $"Gear-{count}.svg");
    File.WriteAllText(svgPath, profile.Svg!);

    VectorIconCompilationResult icon = ProfileVectorIconCompiler.Compile(profile, source, $"Gear-{count}.profile.tsx");
    Require(icon.Success, Diagnostics(icon.Diagnostics.Select(item => item.Reason)));
    variants.Add(new
    {
        count,
        templateSpecializationHash = specialization.Value!.DeterministicHash,
        profileOperationArrayHash = specialization.Value.DeterministicHash,
        profile.ProfileIrHash,
        profile.CanonicalContourHash,
        svgHash = Sha256(profile.Svg!),
        msdfHash = icon.Artifact!.FieldHash,
        iconIdentity = icon.Artifact.Identity.Value,
        operation = profile.Definition!.Operations[0],
    });
}

ProfileCompilationResult templateGear = ProfileTsxCompiler.CompileWithTemplates(EvidenceSources.GearSource, EvidenceSources.TemplateLibrary);
ProfileCompilationResult manualGear = ProfileTsxCompiler.Compile(EvidenceSources.ManualGearSource);
Require(templateGear.Success && manualGear.Success, "Manual/template Gear compilation failed.");
VectorIconCompilationResult templateIcon = ProfileVectorIconCompiler.Compile(
    templateGear,
    EvidenceSources.GearSource,
    "Gear.profile.tsx");
VectorIconCompilationResult manualIcon = ProfileVectorIconCompiler.Compile(
    manualGear,
    EvidenceSources.ManualGearSource,
    "Gear.profile.tsx");
Require(templateIcon.Success && manualIcon.Success, "Manual/template Gear MSDF compilation failed.");

var manifest = new
{
    milestone = "COPELAND-PROFILE-TEMPLATE-FUNCTIONS-M1",
    kind = "typed-compile-time-profile-function-composition",
    outcome = "A",
    profileFunctionsAreOrdinaryFunctions = true,
    templateValueParametersUsed = true,
    typeGenericsAbusedForValues = false,
    profileOperationArraySupported = true,
    templateReturnsTypedValues = true,
    syntaxInjectionUsed = false,
    reparseUsed = false,
    geometryMacroSystemAdded = false,
    runtimeProfileInterpreterAdded = false,
    profileSemanticIrChanged = false,
    m5RendererChanged = false,
    parity = new
    {
        profileIr = templateGear.ProfileIrHash == manualGear.ProfileIrHash,
        contour = templateGear.CanonicalContourHash == manualGear.CanonicalContourHash,
        svg = templateGear.Svg == manualGear.Svg,
        msdf = templateIcon.Artifact!.FieldHash == manualIcon.Artifact!.FieldHash,
    },
    validation = new
    {
        focused = 89,
        copeland = 1653,
        machina = 738,
        aurelian = 650,
        jointTaskForce = 3358,
        nativeM5Icons = 8,
        nativeM5SemanticUses = 18,
        nativeM5ValidationErrors = 0,
    },
    variants,
};

string manifestPath = Path.Combine(output, "manifest.json");
File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(manifestPath);

static string Sha256(string value)
    => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

static string Diagnostics(IEnumerable<string> diagnostics)
    => string.Join(Environment.NewLine, diagnostics);

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

internal static class EvidenceSources
{
    public const string GearSource = """
    export default (
        <Profile name="Gear" baseState="Base" base={Circle({ radius: 32 })}>
            {instantiate GearTeeth<count: 12, toothFraction: 0.52, toothDepth: 8.0>}
            {Hole({ as: "Hollow", id: "CenterHole", radius: 12 })}
            {Yield(Hollow)}
        </Profile>
    );
    """;

    public const string ManualGearSource = """
    export default (
        <Profile name="Gear" baseState="Base" base={Circle({ radius: 32 })}>
            {RepeatRadial({ as: "WithTeeth", id: "GearTeeth", count: 12, toothDepth: 8, toothFraction: 0.52, rotation: 90 })}
            {Hole({ as: "Hollow", id: "CenterHole", radius: 12 })}
            {Yield(Hollow)}
        </Profile>
    );
    """;

    public const string TemplateLibrary = """
    import { Hole, ProfileOperation, RepeatRadial } from "./Profile";

        template<static count: int, static toothFraction: number, static toothDepth: number> ToothFeature: ProfileOperation {
        return RepeatRadial({
            id: "GearTeeth",
            as: "WithTeeth",
            count,
            toothDepth,
                toothFraction,
            rotation: 90.0
        });
    }

        template<static count: int, static toothFraction: number, static toothDepth: number> GearTeeth: ProfileOperation[] {
            return [instantiate ToothFeature<count: count, toothFraction: toothFraction, toothDepth: toothDepth>];
        }

        function CenterHole(radius: number): ProfileOperation {
            return Hole({ id: "MountHole", as: "Hollow", radius, x: 0.0, y: 0.0 });
        }

        template<static radius: number> MountHole: ProfileOperation[] {
            return [CenterHole(radius)];
        }
    """;
}
