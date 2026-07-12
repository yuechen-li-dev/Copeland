using System.CommandLine;
using System.Text.Json;
using Aurelian.Assets;
using Aurelian.AssetTool;

var manifestOption = new Option<FileInfo>("--manifest") { Required = true, Description = "Path to assets.toml manifest file." };
var outputOption = new Option<DirectoryInfo>("--output") { Required = true, Description = "Output directory for generated shader artifacts." };
var diagnosticsOption = new Option<DiagnosticFormat>("--diagnostics") { Description = "Diagnostics output format: text, json, or jsonl." };
var strictDxcOption = new Option<bool>("--strict-dxc") { Description = "Require DXC to be available and emit DXIL diagnostics as fatal." };
var noDxcOption = new Option<bool>("--no-dxc") { Description = "Disable strict DXC checks even if strict mode was requested by defaults." };
var verboseOption = new Option<bool>("--verbose") { Description = "Emit per-artifact build lines." };
var quietOption = new Option<bool>("--quiet") { Description = "Suppress non-diagnostic success output." };

var buildAssetsCommand = new Command("build-assets") { Description = "Build shader assets from an asset manifest." };
buildAssetsCommand.Options.Add(manifestOption);
buildAssetsCommand.Options.Add(outputOption);
buildAssetsCommand.Options.Add(diagnosticsOption);
buildAssetsCommand.Options.Add(strictDxcOption);
buildAssetsCommand.Options.Add(noDxcOption);
buildAssetsCommand.Options.Add(verboseOption);
buildAssetsCommand.Options.Add(quietOption);

buildAssetsCommand.SetAction(parseResult =>
{
    var manifestFile = parseResult.GetValue(manifestOption)!;
    var outputDirectory = parseResult.GetValue(outputOption)!;
    var diagnosticsFormat = parseResult.GetValue(diagnosticsOption);
    var strictDxc = parseResult.GetValue(strictDxcOption);
    var noDxc = parseResult.GetValue(noDxcOption);
    var verbose = parseResult.GetValue(verboseOption);
    var quiet = parseResult.GetValue(quietOption);
    var effectiveStrictDxc = strictDxc && !noDxc;

    var manifestPath = manifestFile.FullName;
    var outputPath = outputDirectory.FullName;

    if (!File.Exists(manifestPath))
    {
        WriteDiagnostics([new AssetDiagnostic("AM001", "error", $"Manifest not found at '{manifestPath}'.", manifestPath)], diagnosticsFormat);
        return 2;
    }

    Directory.CreateDirectory(outputPath);
    var (manifest, parseDiagnostics) = AssetManifestParser.Parse(File.ReadAllText(manifestPath), manifestPath);
    if (parseDiagnostics.Count > 0 || manifest is null)
    {
        WriteDiagnostics(parseDiagnostics, diagnosticsFormat);
        return parseDiagnostics.Any(d => d.Fatal) ? 1 : 0;
    }

    var result = new AssetPipelineRunner().BuildShaders(manifest, manifestPath, outputPath, effectiveStrictDxc);
    if (diagnosticsFormat == DiagnosticFormat.Json)
        Console.WriteLine(JsonSerializer.Serialize(result.Diagnostics.Select(CliDiagnosticFormatter.ToRecord)));
    else
        WriteDiagnostics(result.Diagnostics, diagnosticsFormat);

    if (!quiet)
    {
        foreach (var built in result.Built.Where(_ => verbose || diagnosticsFormat == DiagnosticFormat.Text))
            Console.WriteLine($"OK {built.ShaderId} -> {built.ManifestPath}");

        if (diagnosticsFormat == DiagnosticFormat.Jsonl)
        {
            foreach (var built in result.Built)
                Console.WriteLine(CliDiagnosticFormatter.FormatArtifactRecord(built.ShaderId, built.ManifestPath));
        }
    }

    if (result.Diagnostics.Any(d => d.Fatal))
    {
        Console.WriteLine($"FAILED: {result.Diagnostics.Count(d => d.Fatal)} fatal diagnostics.");
        return 1;
    }

    if (!quiet)
        Console.WriteLine($"SUCCESS: built {result.Built.Count} shader assets.");

    return 0;
});

var root = new RootCommand("Aurelian asset pipeline command-line tool.");
root.Subcommands.Add(buildAssetsCommand);
return await root.Parse(args).InvokeAsync();

static void WriteDiagnostics(IEnumerable<AssetDiagnostic> diagnostics, DiagnosticFormat format)
{
    foreach (var diagnostic in diagnostics)
        Console.WriteLine(CliDiagnosticFormatter.FormatDiagnostic(diagnostic, format));
}
