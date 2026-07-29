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

string generatedDirectory = Path.Combine(projectRoot, "src", "generated");
Directory.CreateDirectory(generatedDirectory);
string sourcePath = Path.Combine(projectRoot, "machina", "LayoutProfiles.machina.ts");
string source = File.ReadAllText(sourcePath);
var layouts = new[]
{
    new LayoutDefinition("Desktop", "DesktopLayout", "desktop", new MachinaRect(0, 0, 1440, 900)),
    new LayoutDefinition("Tablet", "TabletLayout", "tablet", new MachinaRect(0, 0, 768, 1024)),
    new LayoutDefinition("Mobile", "MobileLayout", "mobile", new MachinaRect(0, 0, 390, 1604)),
};
var css = new StringBuilder();

foreach (LayoutDefinition layout in layouts)
{
    MachinaSourceCompilation compilation = MachinaSourceCompiler.Compile(source, sourcePath, layout.FunctionName);
    if (!compilation.Success)
    {
        string diagnostics = string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message));
        throw new InvalidOperationException(diagnostics);
    }

    MachinaResolvedDocument resolved = MachinaLayoutResolver.Resolve(compilation.View!, layout.Viewport);
    MachinaReactArtifact artifact = MachinaBrowserLowerer.LowerForReact(resolved, layout.Namespace);
    css.Append(artifact.Css).AppendLine();
    File.WriteAllText(Path.Combine(generatedDirectory, "Machina" + layout.Name + "Layout.ts"), WriteClassAccessors(artifact, layout.Name));
    File.WriteAllText(Path.Combine(generatedDirectory, "Machina" + layout.Name + "Layout.resolved.txt"), resolved.ToDebugText());
}

File.WriteAllText(Path.Combine(projectRoot, "machina-layouts.generated.css"), css.ToString());
Console.WriteLine("Generated React-facing desktop, tablet, and mobile Machina layout classes and CSS.");

static string WriteClassAccessors(MachinaReactArtifact artifact, string layoutName)
{
    var builder = new StringBuilder(
        "// Generated from machina/LayoutProfiles.machina.ts. Edit Machina source, not this file.\n\n");

    foreach ((string identity, string classes) in artifact.ClassesByIdentity.OrderBy(entry => entry.Key, StringComparer.Ordinal))
    {
        builder.Append("export function ")
            .Append(FunctionName(layoutName, identity))
            .Append("(): string {\n    return \"")
            .Append(classes)
            .Append("\";\n}\n\n");
    }

    return builder.ToString();
}

static string FunctionName(string layoutName, string identity)
{
    string suffix = identity.Replace("root", "Root", StringComparison.Ordinal)
        .Replace('/', '_');
    return "Machina" + layoutName + suffix + "Class";
}

sealed record LayoutDefinition(string Name, string FunctionName, string Namespace, MachinaRect Viewport);
