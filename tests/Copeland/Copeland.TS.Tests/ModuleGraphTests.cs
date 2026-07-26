using Copeland.TS.Compiler;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Diagnostics;
using Copeland.TS.Mir;
using MetadataReference = Microsoft.CodeAnalysis.MetadataReference;
using OutputKind = Microsoft.CodeAnalysis.OutputKind;
using CSharpCompilationOptions = Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions;
using CSharpSyntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree;
using RoslynCSharpCompilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation;
using System.Diagnostics;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ModuleGraphTests
{
    [Fact]
    public void Relative_named_imports_preserve_the_exported_function_and_type_identity()
    {
        CopelandProjectCompilation compilation = Compile(
        [
            ("Recipes/RecipeBook.ts", """
                export record Recipe { name: string; }
                export function Build(recipe: Recipe): string { return recipe.name; }
                """),
            ("Main.ts", """
                import { Build as BuildSummary, Recipe } from "./Recipes/RecipeBook";
                export function Run(name: string): string {
                    const recipe: Recipe = { name, };
                    return BuildSummary(recipe);
                }
                """),
        ]);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("Run", compilation.Compilation!.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-NPM-0001");
        var main = Assert.Single(compilation.MirProjectGraph!.Modules, module => module.Id.Value == "Main.ts");
        Assert.Contains(main.Imports, import => import.TargetModule?.Value == "Recipes/RecipeBook.ts" && import.ExportedName == "Build" && import.LocalName == "BuildSummary");
        Assert.Contains(main.Functions, function => function.Name == "Run");
    }

    [Fact]
    public void Explicit_tsx_import_is_resolved_from_the_project_source_set()
    {
        CopelandProjectCompilation compilation = Compile(
        [
            ("View.tsx", "export function Label(): string { return \"view\"; }"),
            ("Main.ts", """
                import { Label } from "./View.tsx";
                export function Run(): string { return Label(); }
                """),
        ]);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public void Local_and_npm_imports_remain_in_their_separate_resolution_domains()
    {
        var sources = new[]
        {
            new CopelandProjectSource("Math.ts", "Math.ts", "export function Double(value: number): number { return value * 2; }"),
            new CopelandProjectSource("Main.ts", "Main.ts", """
                import { Double } from "./Math";
                import { sum } from "@fixture/math";
                export function Run(): number { return Double(sum(1, 2)); }
                """),
        };
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(sources, new CopelandCompilationOptions
        {
            SourcePath = "Project.ts",
            NpmPackages = [new CopelandNpmPackageContract("@fixture/math", "1.0.0", [new CopelandNpmFunctionContract("sum", ["number", "number"], "number")])],
        });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("npm:@fixture/math", compilation.Compilation!.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_project_owned_module_does_not_fall_back_to_npm()
    {
        CopelandProjectCompilation compilation = Compile(
        [
            ("Main.ts", "import { Build } from \"./Missing\"; export function Run(): string { return \"unreachable\"; }"),
        ]);

        Diagnostic diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("COPE-MODULE-0002", diagnostic.Id);
        Assert.Contains("project-owned", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_exported_declarations_cannot_be_imported()
    {
        CopelandProjectCompilation compilation = Compile(
        [
            ("Library.ts", "function Hidden(): string { return \"hidden\"; }"),
            ("Main.ts", "import { Hidden } from \"./Library\"; export function Run(): string { return Hidden(); }"),
        ]);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-MODULE-0004");
    }

    [Fact]
    public void Foreign_functions_require_an_explicit_named_import()
    {
        CopelandProjectCompilation compilation = Compile(
        [
            ("Library.ts", "function Hidden(): string { return \"hidden\"; }"),
            ("Main.ts", "export function Run(): string { return Hidden(); }"),
        ]);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-MODULE-0007");
    }

    [Fact]
    public void Foreign_private_types_require_an_explicit_named_import()
    {
        CopelandProjectCompilation compilation = Compile(
        [
            ("Library.ts", "record Hidden { value: number; }"),
            ("Main.ts", "export function Run(value: Hidden): number { return value.value; }"),
        ]);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-MODULE-0007");
    }

    [Fact]
    public void Flow_imports_are_explicitly_deferred_until_flow_values_are_designed()
    {
        CopelandProjectCompilation compilation = Compile(
        [
            ("Flow.ts", "export flow Pantry -> number { board { total: number = 0; } event Close(); state Open initial { on Close() -> Done; } state Done { finish board.total; } }"),
            ("Main.ts", "import { Pantry } from './Flow'; export function Run(): number { return 0; }"),
        ]);

        Diagnostic diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("COPE-MODULE-0008", diagnostic.Id);
        Assert.Contains("deferred", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cross_module_records_enums_requirements_generics_and_generators_share_one_mir_graph()
    {
        CopelandProjectCompilation compilation = Compile(
        [
            ("Model.ts", """
                export record Recipe { name: string; portions: number; }
                export enum Decision { Ready(recipe: Recipe), Skip(reason: string), }
                export interface HasPortions { portions: number; }
                export function PortionCount<T extends HasPortions>(value: T): number { return value.portions; }
                export function Decide(recipe: Recipe): number {
                    return match Decision.Ready(recipe) { Ready(value) => value.portions, Skip(reason) => 0, };
                }
                export function* Slots(count: number): Iterable<number> {
                    let current: number = 0;
                    while (current < count) { yield current; current = current + 1; }
                }
                """),
            ("Main.ts", """
                import { Decide, PortionCount, Recipe, Slots } from "./Model";
                export function Run(): number {
                    const recipe: Recipe = { name: "soup", portions: 3, };
                    let total: number = Decide(recipe) + PortionCount(recipe);
                    for (const slot of Slots(2)) { total = total + slot; }
                    return total;
                }
                """),
        ]);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirProjectModule model = Assert.Single(compilation.MirProjectGraph!.Modules, module => module.Id.Value == "Model.ts");
        MirProjectModule main = Assert.Single(compilation.MirProjectGraph.Modules, module => module.Id.Value == "Main.ts");
        Assert.Contains(model.Exports, export => export.Name == "Recipe" && export.DeclarationKind == "record");
        Assert.Contains(model.Exports, export => export.Name == "Decision" && export.DeclarationKind == "enum");
        Assert.Contains(main.Imports, import => import.TargetModule?.Value == "Model.ts" && import.ExportedName == "Recipe");
        Assert.Same(
            Assert.Single(compilation.Compilation!.MirCompilation!.Program!.Functions, function => function.Name == "Decide"),
            Assert.Single(model.Functions, function => function.Name == "Decide"));
    }

    [Fact]
    public void Same_named_module_functions_remain_distinct_symbols_and_execute_through_local_aliases()
    {
        CopelandProjectCompilation project = Compile(
        [
            ("Left/Tools.ts", "export function Normalize(value: number): number { return value + 1; }"),
            ("Right/Tools.ts", "export function Normalize(value: number): number { return value * 2; }"),
            ("Main.ts", "import { Normalize as LeftNormalize } from './Left/Tools'; import { Normalize as RightNormalize } from './Right/Tools'; export function Run(): number { return LeftNormalize(3) + RightNormalize(3); }"),
        ]);

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics));
        MirProjectModule left = Assert.Single(project.MirProjectGraph!.Modules, module => module.Id.Value == "Left/Tools.ts");
        MirProjectModule right = Assert.Single(project.MirProjectGraph.Modules, module => module.Id.Value == "Right/Tools.ts");
        Assert.NotEqual(Assert.Single(left.Functions).Name, Assert.Single(right.Functions).Name);
        Assert.Equal("Normalize", Assert.Single(left.Exports).Name);
        Assert.Equal("Normalize", Assert.Single(right.Exports).Name);

        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(project.MirProjectGraph);
        string directory = Path.Combine(Path.GetTempPath(), "Copeland-Module-Scope-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "package.json"), "{\"type\":\"module\"}");
            foreach ((string path, string content) in emitted.Files)
            {
                string outputPath = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, content);
            }
            Assert.Equal("10", RunNode(directory, "-e", "import { Run } from './Main.js'; console.log(Run());").Output.Trim());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Same_named_records_and_enums_have_module_owned_clr_and_esm_identities()
    {
        CopelandProjectCompilation project = Compile(
        [
            ("Alpha.ts", """
                export record Result { value: number; }
                export enum Status { Ready, Failed, }
                export function Score(value: Result, status: Status): number {
                    return match status { Ready => value.value, Failed => 0, };
                }
                """),
            ("Beta.ts", """
                export record Result { message: string; }
                export enum Status { Open, Closed, }
                export function Score(value: Result, status: Status): number {
                    return match status { Open => 3, Closed => 7, };
                }
                """),
            ("Main.ts", """
                import { Result as AlphaResult, Status as AlphaStatus, Score as AlphaScore } from "./Alpha";
                import { Result as BetaResult, Status as BetaStatus, Score as BetaScore } from "./Beta";
                export function Run(): number {
                    const alpha: AlphaResult = { value: 4, };
                    const beta: BetaResult = { message: "tea", };
                    return AlphaScore(alpha, AlphaStatus.Ready) + BetaScore(beta, BetaStatus.Open);
                }
                """),
        ]);

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics));
        MirProgram mir = project.Compilation!.MirCompilation!.Program!;
        Assert.NotEqual(mir.Records.Single(record => record.Fields.Single().Name == "value").Id,
            mir.Records.Single(record => record.Fields.Single().Name == "message").Id);
        MirEnum[] statuses = mir.Enums.Where(@enum => @enum.Name.EndsWith("_Status", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, statuses.Length);
        Assert.NotEqual(statuses[0].Name, statuses[1].Name);

        Copeland.TS.Backend.CSharp.CSharpCompilation emittedClr = CSharpBackend.Emit(mir);
        Assert.Empty(emittedClr.Diagnostics);
        Assert.True(CompileCSharp(emittedClr.SourceText!), "Generated CLR source did not compile.");

        JavaScriptProjectCompilation emittedJs = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!);
        Assert.True(emittedJs.Success, string.Join(Environment.NewLine, emittedJs.Diagnostics));
        string directory = Path.Combine(Path.GetTempPath(), "Copeland-Module-Types-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "package.json"), "{\"type\":\"module\"}");
            foreach ((string path, string content) in emittedJs.Files)
            {
                string outputPath = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, content);
            }
            ProcessResult result = RunNode(directory, "-e", "import { Run } from './Main.js'; console.log(Run());");
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Equal("7", result.Output.Trim());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Local_module_cycles_report_the_full_cycle_path()
    {
        CopelandProjectCompilation compilation = Compile(
        [
            ("A.ts", "import { B } from \"./B\"; export function A(): string { return B(); }"),
            ("B.ts", "import { A } from \"./A\"; export function B(): string { return A(); }"),
        ]);

        Diagnostic diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("COPE-MODULE-0006", diagnostic.Id);
        Assert.Contains("A.ts → B.ts → A.ts", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JavaScript_project_emission_is_a_real_node_esm_graph_with_local_and_npm_imports()
    {
        var sources = new[]
        {
            new CopelandProjectSource("Recipes/Math.ts", "Recipes/Math.ts", "export function Double(value: number): number { return value * 2; }"),
            new CopelandProjectSource("Admin/Format.ts", "Admin/Format.ts", "export function AdminValue(): number { return 10; }"),
            new CopelandProjectSource("Main.ts", "Main.ts", """
                import { Double as DoubleValue } from "./Recipes/Math";
                import { AdminValue } from "./Admin/Format";
                import { sum } from "@fixture/math";
                export function Run(): number { return DoubleValue(AdminValue() + 1); }
                """),
        };
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(sources, new CopelandCompilationOptions
        {
            SourcePath = "Project.ts",
            NpmPackages = [new CopelandNpmPackageContract("@fixture/math", "1.0.0", [new CopelandNpmFunctionContract("sum", ["number", "number"], "number")])],
        });
        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics));

        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.Contains("Recipes/Math.js", emitted.Files.Keys);
        Assert.Contains("Admin/Format.js", emitted.Files.Keys);
        Assert.Contains("import { Double as DoubleValue } from \"./Recipes/Math.js\";", emitted.Files["Main.js"], StringComparison.Ordinal);
        Assert.Contains("import { sum } from \"@fixture/math\";", emitted.Files["Main.js"], StringComparison.Ordinal);

        string directory = Path.Combine(Path.GetTempPath(), "Copeland-Modules-ESM-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "package.json"), "{\"type\":\"module\"}");
            foreach ((string path, string content) in emitted.Files)
            {
                string outputPath = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, content);
            }

            string packageDirectory = Path.Combine(directory, "node_modules", "@fixture", "math");
            Directory.CreateDirectory(packageDirectory);
            File.WriteAllText(Path.Combine(packageDirectory, "package.json"), "{\"type\":\"module\",\"exports\":\"./index.js\"}");
            File.WriteAllText(Path.Combine(packageDirectory, "index.js"), "export function sum(left, right) { return left + right; }");

            ProcessResult result = RunNode(directory, "-e", "import { Run } from './Main.js'; console.log(Run());");
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("22", result.Output.Trim());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static CopelandProjectCompilation Compile(IReadOnlyList<(string Path, string Source)> sources)
        => CopelandProjectCompiler.CompileToMir(
            sources.Select(source => new CopelandProjectSource(source.Path, source.Path, source.Source)).ToArray(),
            new CopelandCompilationOptions { SourcePath = "Project.ts" });

    private static ProcessResult RunNode(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("node")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output);
    }

    private sealed record ProcessResult(int ExitCode, string Output);

    private static bool CompileCSharp(string source)
    {
        string trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        IEnumerable<MetadataReference> references = trustedAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        using var assembly = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = RoslynCSharpCompilation.Create(
            "ModuleIdentityProof",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(assembly);
        return result.Success;
    }
}
