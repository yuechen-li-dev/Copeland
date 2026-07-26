using Copeland.TS.MachinaSource;
using Copeland.TS.Mir.Machina;

string sampleRoot = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(sampleRoot, "Settings.ts")))
{
    DirectoryInfo? parent = Directory.GetParent(sampleRoot);
    if (parent is null)
    {
        throw new InvalidOperationException("Could not locate the Machina M1 sample root.");
    }

    sampleRoot = parent.FullName;
}

string sourcePath = Path.Combine(sampleRoot, "Settings.ts");
MachinaSourceCompilation compilation = MachinaSourceCompiler.Compile(File.ReadAllText(sourcePath), sourcePath, "SettingsPage");
if (!compilation.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
}

MachinaResolvedDocument resolved = MachinaLayoutResolver.Resolve(compilation.View!, new MachinaRect(0, 0, 400, 240));
string outputDirectory = Path.Combine(sampleRoot, "wwwroot");
Directory.CreateDirectory(outputDirectory);
File.WriteAllText(Path.Combine(outputDirectory, "index.html"), MachinaBrowserPageBuilder.Create(resolved, "Copeland Machina M1 Settings"));
File.WriteAllText(Path.Combine(outputDirectory, "resolved.txt"), resolved.ToDebugText());
Console.WriteLine($"Machina settings page written to {outputDirectory}");
