using System.Text;
using Copeland.TS.MachinaSource;
using Copeland.TS.Mir.Machina;

string projectRoot = Directory.GetParent(AppContext.BaseDirectory)!.FullName;
while (!File.Exists(Path.Combine(projectRoot, "manifest.tsx")))
{
    DirectoryInfo? parent = Directory.GetParent(projectRoot);
    if (parent is null)
    {
        throw new InvalidOperationException("Could not locate the Copeland website project root.");
    }

    projectRoot = parent.FullName;
}

string sourcePath = Path.Combine(projectRoot, "machina", "Hero.machina.ts");
MachinaSourceCompilation compilation = MachinaSourceCompiler.Compile(
    File.ReadAllText(sourcePath),
    sourcePath,
    "HeroLayout");
if (!compilation.Success)
{
    string diagnostics = string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message));
    throw new InvalidOperationException(diagnostics);
}

MachinaResolvedDocument resolved = MachinaLayoutResolver.Resolve(
    compilation.View!,
    new MachinaRect(0, 0, 940, 555));
MachinaReactArtifact artifact = MachinaBrowserLowerer.LowerForReact(resolved);

string generatedDirectory = Path.Combine(projectRoot, "src", "generated");
Directory.CreateDirectory(generatedDirectory);
File.WriteAllText(Path.Combine(projectRoot, "machina-hero.generated.css"), artifact.Css);
File.WriteAllText(Path.Combine(generatedDirectory, "MachinaHero.ts"), WriteClassAccessors(artifact));
File.WriteAllText(Path.Combine(generatedDirectory, "MachinaHero.resolved.txt"), resolved.ToDebugText());
Console.WriteLine("Generated React-facing Machina hero classes and CSS.");

static string WriteClassAccessors(MachinaReactArtifact artifact)
{
    var builder = new StringBuilder(
        "// Generated from machina/Hero.machina.ts. Edit Machina source, not this file.\n\n");

    foreach ((string identity, string classes) in artifact.ClassesByIdentity.OrderBy(entry => entry.Key, StringComparer.Ordinal))
    {
        builder.Append("export function ")
            .Append(FunctionName(identity))
            .Append("(): string {\n    return \"")
            .Append(classes)
            .Append("\";\n}\n\n");
    }

    return builder.ToString();
}

static string FunctionName(string identity)
{
    string suffix = identity.Replace("root", "Root", StringComparison.Ordinal)
        .Replace('/', '_');
    return "MachinaHero" + suffix + "Class";
}
