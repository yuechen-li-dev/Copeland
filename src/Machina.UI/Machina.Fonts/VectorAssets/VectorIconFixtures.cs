using Machina.Core.Assets;

using Copeland.Profile;

namespace Machina.VectorAssets;

public sealed record VectorIconFixture(string Name, string Source, string Provenance);

public static class VectorIconFixtures
{
    public static IReadOnlyList<VectorIconFixture> Canonical { get; } =
    [
        new("Settings", """
            <svg viewBox="0 0 24 24"><path d="M9 1 L15 1 L16 5 L19 3 L23 7 L20 10 L23 12 L21 17 L17 16 L17 21 L11 23 L9 19 L6 21 L2 17 L4 14 L1 11 L3 6 L7 7 Z M9 12 C9 8 15 8 15 12 C15 16 9 16 9 12 Z"/></svg>
            """, "Self-authored Copeland M5 qualification fixture."),
        new("Play", """
            <svg viewBox="0 0 24 24"><path d="M5 3 L21 12 L5 21 Z"/></svg>
            """, "Self-authored Copeland M5 qualification fixture."),
        new("Pause", """
            <svg viewBox="0 0 24 24"><path d="M5 3 L10 3 L10 21 L5 21 Z M14 3 L19 3 L19 21 L14 21 Z"/></svg>
            """, "Self-authored Copeland M5 qualification fixture."),
        new("Check", """
            <svg viewBox="0 0 24 24"><path d="M2 12 L6 8 L10 12 L18 4 L22 8 L10 20 Z"/></svg>
            """, "Self-authored Copeland M5 qualification fixture."),
        new("Close", """
            <svg viewBox="0 0 24 24"><g transform="rotate(45 12 12)"><path d="M9 1 L15 1 L15 9 L23 9 L23 15 L15 15 L15 23 L9 23 L9 15 L1 15 L1 9 L9 9 Z"/></g></svg>
            """, "Self-authored Copeland M5 qualification fixture; includes source rotation."),
        new("Heart", """
            <svg viewBox="0 0 24 24"><path d="M12 22 C10 19 2 15 2 8 C2 2 9 1 12 6 C15 1 22 2 22 8 C22 15 14 19 12 22 Z"/></svg>
            """, "Self-authored Copeland M5 qualification fixture."),
        new("InfoCircle", """
            <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="11"/><circle cx="12" cy="12" r="7"/><path d="M10 9 L14 9 L14 19 L10 19 Z M10 4 L14 4 L14 7 L10 7 Z"/></svg>
            """, "Self-authored Copeland M5 qualification fixture; nested contours exercise non-zero orientation."),
        new("Folder", """
            <svg viewBox="0 0 32 20"><path d="M1 3 L12 3 L15 6 L31 6 L31 19 L1 19 Z"/><path d="M3 1 L11 1 L13 3 L3 3 Z"/></svg>
            """, "Self-authored Copeland M5 qualification fixture; intentionally wide."),
    ];

    public static IReadOnlyDictionary<string, VectorIconMsdfArtifact> CompileCanonical(
        VectorIconCompilationSettings? settings = null)
    {
        settings ??= new VectorIconCompilationSettings();
        Dictionary<string, VectorIconMsdfArtifact> result = new(StringComparer.Ordinal);
        foreach (VectorIconFixture fixture in Canonical)
        {
            VectorIconCompilationResult compilation = Compile(fixture, settings);
            if (!compilation.Success)
            {
                string message = string.Join("; ", compilation.Diagnostics.Select(static diagnostic => diagnostic.Reason));
                throw new InvalidOperationException($"Canonical vector icon '{fixture.Name}' did not compile: {message}");
            }
            result.Add(fixture.Name, compilation.Artifact!);
        }
        return result;
    }

    public static VectorIconCompilationResult Compile(
        VectorIconFixture fixture,
        VectorIconCompilationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        settings ??= new VectorIconCompilationSettings();
        if (fixture.Name == "Settings")
        {
            const string semanticSource = "Circle(radius:32) -> RepeatRadial(count:12,toothDepth:8) -> Hole(radius:12)";
            ProfileCompilationResult profile = ProfileCompiler.Compile(ProfileFixtures.Gear());
            return ProfileVectorIconCompiler.Compile(profile, semanticSource, "Settings.profile.tsx", settings);
        }

        return VectorIconMsdfCompiler.CompileSvg(fixture.Source, fixture.Name + ".svg", settings);
    }
}

public sealed class VectorIcons
{
    private readonly IReadOnlyDictionary<string, VectorIconMsdfArtifact> artifacts;

    public VectorIcons(IReadOnlyDictionary<string, VectorIconMsdfArtifact> artifacts)
    {
        this.artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    }

    public MachinaVectorIconId Settings => Get("Settings");

    public MachinaVectorIconId Play => Get("Play");

    public MachinaVectorIconId Pause => Get("Pause");

    public MachinaVectorIconId Check => Get("Check");

    public MachinaVectorIconId Close => Get("Close");

    public MachinaVectorIconId Heart => Get("Heart");

    public MachinaVectorIconId InfoCircle => Get("InfoCircle");

    public MachinaVectorIconId Folder => Get("Folder");

    private MachinaVectorIconId Get(string name)
    {
        return artifacts.TryGetValue(name, out VectorIconMsdfArtifact? artifact)
            ? artifact.Identity
            : throw new InvalidOperationException($"Vector icon registry is missing '{name}'.");
    }
}
