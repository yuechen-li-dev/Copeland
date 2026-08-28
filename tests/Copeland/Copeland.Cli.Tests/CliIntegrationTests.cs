using Xunit;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Tson;

namespace Copeland.Cli.Tests;

public sealed class CliIntegrationTests
{
    [Fact]
    public async Task Template_cli_binds_defaulted_type_and_named_static_parameters_through_shared_evaluator()
    {
        using var temp = new TempDir();
        string source = temp.WriteFile(
            "Template.ts",
            "interface Named { name: string; } record Standard { name: string; } template<type T extends Named = Standard, static name: string, static target: string = \"net10.0\"> App: ProjectTree { emit(textFile(`${name}-${nameOf<T>()}.txt`, target)); }");
        string output = Path.Combine(temp.Path, "generated");

        CliResult result = await RunCliAsync(
            temp.Path,
            "template",
            "materialize",
            source,
            "--entry",
            "App",
            "--name",
            "Hello",
            "--target",
            "net10.0",
            "--output",
            output);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("net10.0", await File.ReadAllTextAsync(Path.Combine(output, "Hello-Standard.txt")));
    }

    [Fact]
    public async Task Tscl_build_emits_a_multi_module_production_node_project_and_machine_readable_manifest()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        string greetingPath = temp.WriteFile("src/Greeting.ts", "export function Greeting(name: string): string { return `Hello, ${name}`; }");
        string mainPath = temp.WriteFile("src/Main.ts", "import { Greeting } from \"./Greeting\"; export function Main(): string { return Greeting(\"TSPack\"); }");
        string outputDirectory = Path.Combine(temp.Path, "dist");
        string projectPath = temp.WriteFile("project.json", JsonSerializer.Serialize(new
        {
            projectRoot = temp.Path,
            sources = new[]
            {
                new { logicalPath = "src/Greeting.ts", path = greetingPath },
                new { logicalPath = "src/Main.ts", path = mainPath },
            },
            entry = new { module = "src/Main.ts", @export = "Main" },
            javascriptRuntime = "node",
            javascriptProfile = "production",
            outputDirectory,
            entryOutputPath = "main.js",
        }));
        string resultPath = Path.Combine(temp.Path, "result.json");

        CliResult build = await RunCliAsync(temp.Path, "build", "--project", projectPath, "--result", resultPath);

        Assert.Equal(0, build.ExitCode);
        using JsonDocument result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("main.js", result.RootElement.GetProperty("entryOutputPath").GetString());
        Assert.Contains(result.RootElement.GetProperty("outputs").EnumerateArray(), output => output.GetProperty("path").GetString() == "src/Main.js");
        Assert.Contains("import { Greeting } from \"./Greeting.js\";", await File.ReadAllTextAsync(Path.Combine(outputDirectory, "src", "Main.js")), StringComparison.Ordinal);
        Assert.DoesNotContain("__cope_validate", await File.ReadAllTextAsync(Path.Combine(outputDirectory, "src", "Main.js")), StringComparison.Ordinal);

        CliResult execution = await RunExecutableAsync("node", temp.Path, Path.Combine(outputDirectory, "main.js"));
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("Hello, TSPack\n", execution.StdOut);
    }

    [Fact]
    public async Task Tscl_standalone_build_uses_manifest_config_and_local_environment_without_TSPack()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        temp.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        temp.WriteFile("src/Greeting.ts", "export function Greeting(name: string): string { return `Hello, ${name}`; }");
        temp.WriteFile("src/Main.ts", "import { Greeting } from \"./Greeting\"; export function Main(): string { return Greeting(\"standalone\"); }");
        temp.WriteFile("tsconfig.tsx", """
            import { defineTypeScriptWorkspace } from "copeland/workspace";
            export default defineTypeScriptWorkspace({
                ownership: "partial",
                tscl: { project: "./App.csproj", include: ["src/**"] }
            });
            """);
        temp.WriteFile("manifest.tsx", """
            import { Package, Targets, Workspace, define } from "tspack/manifest";
            export default define(
              <Workspace name="standalone" runtime="nodejs">
                <Package name="standalone" version="1.0.0" kind="app" dependencies={{ values: [] }}>
                  <Targets rows={[{ name: "app", entry: "src/Main.ts", runtime: "dist/main.js", deps: [], peers: [] }]} />
                </Package>
              </Workspace>,
            );
            """);
        string resultPath = Path.Combine(temp.Path, "result.json");

        CliResult build = await RunCliAsync(
            temp.Path,
            "build",
            "--standalone",
            temp.Path,
            "--result",
            resultPath);

        Assert.Equal(0, build.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, ".tspack")));
        CliResult execution = await RunExecutableAsync("node", temp.Path, Path.Combine(temp.Path, "dist", "main.js"));
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("Hello, standalone\n", execution.StdOut);
    }

    [Fact]
    public async Task Tscl_build_emits_a_multi_module_production_browser_project_without_node_launcher_artifacts()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        string statePath = temp.WriteFile(
            "src/State.ts",
            "export function Status(count: number): string { return `Count: ${count}`; }");
        string mainPath = temp.WriteFile(
            "src/Main.ts",
            "import { setText } from \"@copeland/browser-v1\"; import { Status } from \"./State\"; export function Main(): void { setText(\"status\", Status(0)); }");
        string outputDirectory = Path.Combine(temp.Path, "dist");
        string projectPath = temp.WriteFile("project.json", JsonSerializer.Serialize(new
        {
            projectRoot = temp.Path,
            sources = new[]
            {
                new { logicalPath = "src/Main.ts", path = mainPath },
                new { logicalPath = "src/State.ts", path = statePath },
            },
            entry = new { module = "src/Main.ts", @export = "Main" },
            javascriptRuntime = "browser",
            javascriptProfile = "production",
            outputDirectory,
            entryOutputPath = "main.js",
        }));
        string resultPath = Path.Combine(temp.Path, "result.json");

        CliResult build = await RunCliAsync(temp.Path, "build", "--project", projectPath, "--result", resultPath);

        Assert.Equal(0, build.ExitCode);
        using JsonDocument result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("browser", result.RootElement.GetProperty("target").GetString());
        Assert.Contains(result.RootElement.GetProperty("outputs").EnumerateArray(), output => output.GetProperty("path").GetString() == "src/Main.js");
        Assert.Contains("import { setText } from \"@copeland/browser-v1\";", await File.ReadAllTextAsync(Path.Combine(outputDirectory, "src", "Main.js")), StringComparison.Ordinal);
        Assert.Contains("import { Status } from \"./State.js\";", await File.ReadAllTextAsync(Path.Combine(outputDirectory, "src", "Main.js")), StringComparison.Ordinal);
        Assert.Contains("await Main();", await File.ReadAllTextAsync(Path.Combine(outputDirectory, "main.js")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(outputDirectory, "package.json")));
    }

    [Fact]
    public async Task Callable_reference_cli_emission_is_repeatable_executable_and_preserves_stale_artifacts()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("callable.ts", """
            type Operation = (value: number) => number;
            function increment(value: number): number { return value + 1; }
            function identity<T>(value: T): T { return value; }
            function apply(operation: Operation, value: number): number { return operation(value); }
            function main(): number {
                const operation: Operation = increment;
                const same: Operation = identity<number>;
                return apply(operation, same(4));
            }
            """);

        foreach ((string emit, string fileName, string[] extraArguments) in new[]
        {
            ("mir", "callable.cope", Array.Empty<string>()),
            ("csharp", "callable.g.cs", Array.Empty<string>()),
            ("javascript", "callable.g.js", Array.Empty<string>()),
            ("javascript", "callable.sym.js", new[] { "--javascript-profile", "symbolic" }),
        })
        {
            string outputPath = Path.Combine(temp.Path, fileName);
            CliResult first = await RunCliAsync(temp.Path, ["compile", inputPath, "--emit", emit, .. extraArguments, "--out", outputPath]);
            byte[] firstBytes = await File.ReadAllBytesAsync(outputPath);
            CliResult second = await RunCliAsync(temp.Path, ["compile", inputPath, "--emit", emit, .. extraArguments, "--out", outputPath]);

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);
            Assert.Equal(firstBytes, await File.ReadAllBytesAsync(outputPath));
            Assert.DoesNotContain(temp.Path, Encoding.UTF8.GetString(firstBytes), StringComparison.OrdinalIgnoreCase);
        }

        string diagnosticPath = Path.Combine(temp.Path, "callable.g.js");
        string symbolicPath = Path.Combine(temp.Path, "callable.sym.js");
        await File.AppendAllTextAsync(diagnosticPath, "console.log(main());\n", new UTF8Encoding(false));
        await File.AppendAllTextAsync(symbolicPath, "console.log(main());\n", new UTF8Encoding(false));
        Assert.Equal("5\n", (await RunExecutableAsync("node", temp.Path, diagnosticPath)).StdOut);
        Assert.Equal("5\n", (await RunExecutableAsync("node", temp.Path, symbolicPath)).StdOut);

        string stalePath = diagnosticPath;
        string staleHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(stalePath)));
        string invalidPath = temp.WriteFile("invalid-callable.ts", "function generic<T>(value: T): T { return value; } function main(): number { const value = generic; return 0; }");
        CliResult staleFailure = await RunCliAsync(temp.Path, "compile", invalidPath, "--emit", "javascript", "--out", stalePath);
        string freshPath = Path.Combine(temp.Path, "fresh-callable.cope");
        CliResult freshFailure = await RunCliAsync(temp.Path, "compile", invalidPath, "--emit", "mir", "--out", freshPath);

        Assert.Equal(1, staleFailure.ExitCode);
        Assert.Contains("COPE-CALL-0003", staleFailure.StdErr, StringComparison.Ordinal);
        Assert.Equal(staleHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(stalePath))));
        Assert.Equal(1, freshFailure.ExitCode);
        Assert.False(File.Exists(freshPath));
        Assert.DoesNotContain(temp.Path, staleFailure.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nominal_union_cli_emission_is_repeatable_profile_independent_and_preserves_stale_artifacts()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("union.ts", """
            record Circle { radius: number; }
            record Rectangle { width: number; height: number; }
            type Shape = Circle | Rectangle;
            function main(): number {
              const circle: Circle = { radius: 4 };
              const shape: Shape = circle;
              return match shape {
                Circle(value) => value.radius * value.radius,
                Rectangle(value) => value.width * value.height,
              };
            }
            """);

        foreach ((string emit, string fileName, string[] extraArguments) in new[]
        {
            ("mir", "union.cope", Array.Empty<string>()),
            ("csharp", "union.g.cs", Array.Empty<string>()),
            ("javascript", "union.g.js", Array.Empty<string>()),
            ("javascript", "union.sym.js", new[] { "--javascript-profile", "symbolic" }),
        })
        {
            string outputPath = Path.Combine(temp.Path, fileName);
            string[] arguments = ["compile", inputPath, "--emit", emit, .. extraArguments, "--out", outputPath];
            CliResult first = await RunCliAsync(temp.Path, arguments);
            byte[] firstBytes = await File.ReadAllBytesAsync(outputPath);
            CliResult second = await RunCliAsync(temp.Path, arguments);

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);
            Assert.Equal(firstBytes, await File.ReadAllBytesAsync(outputPath));
            Assert.DoesNotContain(temp.Path, Encoding.UTF8.GetString(firstBytes), StringComparison.OrdinalIgnoreCase);
        }

        string diagnosticPath = Path.Combine(temp.Path, "union.g.js");
        string symbolicPath = Path.Combine(temp.Path, "union.sym.js");
        await File.AppendAllTextAsync(diagnosticPath, "console.log(main());\n", new UTF8Encoding(false));
        CliResult execution = await RunExecutableAsync("node", temp.Path, diagnosticPath);
        CliResult symbolicSyntax = await RunExecutableAsync("node", temp.Path, "--check", symbolicPath);
        await File.AppendAllTextAsync(symbolicPath, "console.log(main());\n", new UTF8Encoding(false));
        CliResult symbolicExecution = await RunExecutableAsync("node", temp.Path, symbolicPath);
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("16\n", execution.StdOut);
        Assert.Equal(0, symbolicSyntax.ExitCode);
        Assert.Equal(0, symbolicExecution.ExitCode);
        Assert.Equal("16\n", symbolicExecution.StdOut);

        byte[] staleBytes = await File.ReadAllBytesAsync(diagnosticPath);
        string invalidPath = temp.WriteFile("invalid.ts", "type Shape = Circle | Circle;");
        CliResult staleFailure = await RunCliAsync(temp.Path, "compile", invalidPath, "--emit", "javascript", "--out", diagnosticPath);
        string freshPath = Path.Combine(temp.Path, "fresh.cope");
        CliResult freshFailure = await RunCliAsync(temp.Path, "compile", invalidPath, "--emit", "mir", "--out", freshPath);

        Assert.Equal(1, staleFailure.ExitCode);
        Assert.Contains("COPE-UNION-0004", staleFailure.StdErr, StringComparison.Ordinal);
        Assert.Equal(staleBytes, await File.ReadAllBytesAsync(diagnosticPath));
        Assert.Equal(1, freshFailure.ExitCode);
        Assert.False(File.Exists(freshPath));
        Assert.Equal(staleFailure.StdErr, freshFailure.StdErr);
    }

    [Fact]
    public async Task Transparent_alias_cli_emission_is_erased_repeatable_and_preserves_artifact_policy_on_failure()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("alias.ts", """
            type UserId = number;
            type UserIds = UserId[];
            function retain(values: UserIds): number[] { return values; }
            """);

        foreach ((string emit, string fileName) in new[]
        {
            ("mir", "alias.cope"),
            ("csharp", "alias.g.cs"),
            ("javascript", "alias.g.js"),
        })
        {
            string outputPath = Path.Combine(temp.Path, fileName);
            CliResult first = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emit, "--out", outputPath);
            byte[] firstBytes = await File.ReadAllBytesAsync(outputPath);
            CliResult second = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emit, "--out", outputPath);
            byte[] secondBytes = await File.ReadAllBytesAsync(outputPath);

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);
            Assert.Equal(firstBytes, secondBytes);
            Assert.DoesNotContain("UserId", Encoding.UTF8.GetString(firstBytes), StringComparison.Ordinal);
        }

        string stalePath = Path.Combine(temp.Path, "alias.g.js");
        byte[] staleBytes = await File.ReadAllBytesAsync(stalePath);
        string invalidPath = temp.WriteFile("invalid.ts", "type A = B; type B = A;");
        CliResult staleFailure = await RunCliAsync(
            temp.Path,
            "compile",
            invalidPath,
            "--emit",
            "javascript",
            "--out",
            stalePath);
        string freshPath = Path.Combine(temp.Path, "fresh.cope");
        CliResult freshFailure = await RunCliAsync(
            temp.Path,
            "compile",
            invalidPath,
            "--emit",
            "mir",
            "--out",
            freshPath);

        Assert.Equal(1, staleFailure.ExitCode);
        Assert.Contains("COPE-ALIAS-0005", staleFailure.StdErr, StringComparison.Ordinal);
        Assert.Equal(staleBytes, await File.ReadAllBytesAsync(stalePath));
        Assert.Equal(1, freshFailure.ExitCode);
        Assert.False(File.Exists(freshPath));
    }

    [Fact]
    public async Task Inferred_generic_cli_emission_is_repeatable_executable_and_preserves_stale_artifacts()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", """
            interface Positioned {
                x: number;
                y: number;
            }

            record Point {
                x: number;
                y: number;
            }

            function sum<T extends Positioned>(value: T): number {
                return value.x + value.y;
            }

            function identity<T>(value: T): T {
                return value;
            }

            function mainSum(): number {
                const point: Point = { x: 20, y: 22 };
                return sum(point);
            }

            function mainIdentity(): number {
                return identity<number>(42);
            }
            """);

        var emissions = new[]
        {
            (Emit: "mir", File: "main.cope", Length: 626, Hash: "D0C536F3951C1A1955F985FECF5DA2098D0DB4FC8760234CB53D94A34F79EB93"),
            (Emit: "csharp", File: "main.g.cs", Length: 1085, Hash: "BCABF245706A41808844969864760D7C95E88CC4DB97D68989F0B0DB969549AF"),
            (Emit: "javascript", File: "main.g.js", Length: 2577, Hash: "6F670D0F21F0B27BD1CAE7898024FCD60C9DD912373FC6C464B71E83BF1DBF81"),
        };

        foreach ((string emit, string fileName, int expectedLength, string expectedHash) in emissions)
        {
            string outputPath = Path.Combine(temp.Path, fileName);
            CliResult first = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emit, "--out", outputPath);
            byte[] firstBytes = await File.ReadAllBytesAsync(outputPath);
            CliResult second = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emit, "--out", outputPath);
            byte[] secondBytes = await File.ReadAllBytesAsync(outputPath);

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);
            Assert.Equal(firstBytes, secondBytes);
            Assert.Equal(expectedLength, firstBytes.Length);
            Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(firstBytes)));
            string emitted = Encoding.UTF8.GetString(firstBytes);
            Assert.DoesNotContain(temp.Path, emitted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("interface Positioned", emitted, StringComparison.Ordinal);
            Assert.DoesNotContain("TypeParameter", emitted, StringComparison.Ordinal);
        }

        string javaScriptPath = Path.Combine(temp.Path, "main.g.js");
        await File.AppendAllTextAsync(
            javaScriptPath,
            "process.stdout.write(String(mainSum()) + \"|\" + String(mainIdentity()));\n",
            new UTF8Encoding(false));
        CliResult javaScriptExecution = await RunExecutableAsync("node", temp.Path, javaScriptPath);
        Assert.Equal(0, javaScriptExecution.ExitCode);
        Assert.Equal("42|42", javaScriptExecution.StdOut);

        string runnerProject = temp.WriteFile(
            "runner.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.g.cs" />
                <Compile Include="Runner.cs" />
              </ItemGroup>
            </Project>
            """);
        temp.WriteFile(
            "Runner.cs",
            """
            Console.Write(Copeland.Generated.CopelandModule.mainSum());
            Console.Write("|");
            Console.Write(Copeland.Generated.CopelandModule.mainIdentity());
            """);
        CliResult csharpExecution = await RunExecutableAsync("dotnet", temp.Path, "run", "--project", runnerProject);
        Assert.Equal(0, csharpExecution.ExitCode);
        Assert.Equal("42|42", csharpExecution.StdOut);

        string stalePath = Path.Combine(temp.Path, "main.g.js");
        string staleHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(stalePath)));
        string invalidPath = temp.WriteFile("invalid.ts", """
            interface Positioned {
                x: number;
            }

            function sum<T extends Positioned>(value: T): number {
                return value.y;
            }

            const answer: number = sum<number>(1);
            """);
        CliResult staleFailure = await RunCliAsync(temp.Path, "compile", invalidPath, "--emit", "javascript", "--out", stalePath);
        string freshFailurePath = Path.Combine(temp.Path, "fresh-failure.cope");
        CliResult freshFailure = await RunCliAsync(temp.Path, "compile", invalidPath, "--emit", "mir", "--out", freshFailurePath);

        Assert.Equal(1, staleFailure.ExitCode);
        Assert.Equal(1, freshFailure.ExitCode);
        Assert.Equal(staleHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(stalePath))));
        Assert.False(File.Exists(freshFailurePath));
        Assert.DoesNotContain(temp.Path, staleFailure.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Closed_generic_frontend_diagnostics_are_backend_and_profile_independent()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("invalid.ts", """
            interface Positioned {
                x: number;
            }

            function sum<T extends Positioned>(value: T): number {
                return value.y;
            }

            const answer: number = sum<number>(1);
            """);

        CliResult mir = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "mir");
        CliResult csharp = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp");
        CliResult javascript = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript");
        CliResult symbolic = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--javascript-profile", "symbolic");

        Assert.Equal(1, mir.ExitCode);
        Assert.Equal(mir.StdErr, csharp.StdErr);
        Assert.Equal(mir.StdErr, javascript.StdErr);
        Assert.Equal(mir.StdErr, symbolic.StdErr);
        Assert.Contains("COPE-REQUIREMENT-0004", mir.StdErr, StringComparison.Ordinal);
        Assert.Contains("COPE-REQUIREMENT-0005", mir.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mir", "tson-plan tson0")]
    [InlineData("csharp", "__TsonWriter")]
    [InlineData("javascript", "function makeWriter(")]
    public async Task Compile_emits_runtime_Tson_encoding_for_every_target(string emitTarget, string expectedToken)
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", """
            const $schema: string = "copeland://tests/cli-encoding";
            record Settings { value: string; }
            function encode(value: Settings): string ! TsonEncodeError { return tsonEncode(value); }
            """);

        CliResult first = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emitTarget);
        CliResult second = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emitTarget);

        Assert.Equal(0, first.ExitCode);
        Assert.Contains(expectedToken, first.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("tsonEncode(", first.StdOut, StringComparison.Ordinal);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Array_m1_corpus_cli_emission_is_fresh_repeatable_and_preserves_stale_artifacts_on_failure()
    {
        string corpus = Path.Combine(
            GetRepoRoot(),
            "tests",
            "Copeland",
            "Copeland.TS.Tests",
            "TsonEncoding",
            "Corpus",
            "arrays");
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", await File.ReadAllTextAsync(Path.Combine(corpus, "main.ts")));
        temp.WriteFile("packet.obj.ts", await File.ReadAllTextAsync(Path.Combine(corpus, "packet.obj.ts")));

        var emissions = new[]
        {
            (Emit: "mir", File: "main.cope"),
            (Emit: "csharp", File: "main.g.cs"),
            (Emit: "javascript", File: "main.g.js"),
        };
        foreach ((string emit, string fileName) in emissions)
        {
            string outputPath = Path.Combine(temp.Path, fileName);
            CliResult first = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emit, "--out", outputPath);
            byte[] firstBytes = await File.ReadAllBytesAsync(outputPath);
            CliResult second = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emit, "--out", outputPath);
            byte[] secondBytes = await File.ReadAllBytesAsync(outputPath);

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);
            Assert.Equal(firstBytes, secondBytes);
            Assert.Equal(
                Normalize(await File.ReadAllTextAsync(Path.Combine(corpus, fileName))),
                Normalize(Encoding.UTF8.GetString(firstBytes)));
            string emitted = Encoding.UTF8.GetString(firstBytes);
            Assert.DoesNotContain("packet.obj.ts", emitted, StringComparison.Ordinal);
            Assert.DoesNotContain(temp.Path, emitted, StringComparison.OrdinalIgnoreCase);
        }

        string stalePath = Path.Combine(temp.Path, "main.g.js");
        string staleHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(stalePath)));
        string invalidPath = temp.WriteFile("invalid.ts", """
            const $schema: string = "copeland://corpus/runtime-array-encoding";
            record Root { values: number[]; }
            function encode(value: Root): string ! TsonEncodeError { return tsonEncode(value); }
            const invalid: Root = { values: ["wrong"], };
            """);
        CliResult staleFailure = await RunCliAsync(temp.Path, "compile", invalidPath, "--emit", "javascript", "--out", stalePath);
        string freshFailurePath = Path.Combine(temp.Path, "fresh-failure.g.cs");
        CliResult freshFailure = await RunCliAsync(temp.Path, "compile", invalidPath, "--emit", "csharp", "--out", freshFailurePath);

        Assert.Equal(1, staleFailure.ExitCode);
        Assert.Equal(1, freshFailure.ExitCode);
        Assert.Equal(staleHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(stalePath))));
        Assert.False(File.Exists(freshFailurePath));
    }

    [Fact]
    public async Task Table_m2_corpus_cli_emission_is_fresh_repeatable_executable_and_preserves_stale_artifacts_on_failure()
    {
        string corpus = Path.Combine(
            GetRepoRoot(),
            "tests",
            "Copeland",
            "Copeland.TS.Tests",
            "TsonEncoding",
            "Corpus",
            "tables-m2");
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", await File.ReadAllTextAsync(Path.Combine(corpus, "main.ts")));
        temp.WriteFile("samples.obj.ts", await File.ReadAllTextAsync(Path.Combine(corpus, "samples.obj.ts")));
        temp.WriteFile("empty.obj.ts", await File.ReadAllTextAsync(Path.Combine(corpus, "empty.obj.ts")));

        var emissions = new[]
        {
            (Emit: "mir", File: "main.cope"),
            (Emit: "csharp", File: "main.g.cs"),
            (Emit: "javascript", File: "main.g.js"),
        };
        foreach ((string emit, string fileName) in emissions)
        {
            string outputPath = Path.Combine(temp.Path, fileName);
            CliResult first = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emit, "--out", outputPath);
            byte[] firstBytes = await File.ReadAllBytesAsync(outputPath);
            CliResult second = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emit, "--out", outputPath);
            byte[] secondBytes = await File.ReadAllBytesAsync(outputPath);

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);
            Assert.Equal(firstBytes, secondBytes);
            Assert.Equal(
                Normalize(await File.ReadAllTextAsync(Path.Combine(corpus, fileName))),
                Normalize(Encoding.UTF8.GetString(firstBytes)));
            string emitted = Encoding.UTF8.GetString(firstBytes);
            Assert.DoesNotContain("samples.obj.ts", emitted, StringComparison.Ordinal);
            Assert.DoesNotContain("empty.obj.ts", emitted, StringComparison.Ordinal);
            Assert.DoesNotContain(temp.Path, emitted, StringComparison.OrdinalIgnoreCase);
        }

        string javascriptPath = Path.Combine(temp.Path, "main.g.js");
        await File.AppendAllTextAsync(
            javascriptPath,
            "process.stdout.write(encode().$payload[0] + \"---\\n\" + encodeEmpty().$payload[0]);\n",
            new UTF8Encoding(false));
        CliResult javaScriptExecution = await RunExecutableAsync("node", temp.Path, javascriptPath);
        Assert.Equal(0, javaScriptExecution.ExitCode);
        string expectedEmpty = TsonCanonicalPrinter.Print(
            TsonDocumentReader.ReadSelfDescribed(
                await File.ReadAllTextAsync(Path.Combine(corpus, "empty.obj.ts")),
                TsonDocumentProfile.ObjectTypeScript).Document!);
        Assert.Equal(
            await File.ReadAllTextAsync(Path.Combine(corpus, "expected.tson"))
            + "---\n"
            + expectedEmpty,
            javaScriptExecution.StdOut);

        string runnerProject = temp.WriteFile(
            "runner.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <NoWarn>CS8602</NoWarn>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.g.cs" />
                <Compile Include="Runner.cs" />
              </ItemGroup>
            </Project>
            """);
        temp.WriteFile(
            "Runner.cs",
            """
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            Console.Write(Copeland.Generated.CopelandModule.encode().Value);
            Console.Write("---\n");
            Console.Write(Copeland.Generated.CopelandModule.encodeEmpty().Value);
            """);
        CliResult csharpExecution = await RunExecutableAsync("dotnet", temp.Path, "run", "--project", runnerProject);
        Assert.Equal(0, csharpExecution.ExitCode);
        Assert.Equal(javaScriptExecution.StdOut, csharpExecution.StdOut);

        string stalePath = Path.Combine(temp.Path, "main.g.js");
        string staleHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(stalePath)));
        string invalidPath = temp.WriteFile("invalid.ts", """
            const $schema: string = "copeland://corpus/runtime-table-encoding";
            record table Samples from tsonAsset("./samples.obj.ts") {
                active: string;
            }
            function encode(): string ! TsonEncodeError { return tsonEncode(Samples); }
            """);
        CliResult staleFailure = await RunCliAsync(temp.Path, "compile", invalidPath, "--emit", "javascript", "--out", stalePath);
        string freshFailurePath = Path.Combine(temp.Path, "fresh-failure.cope");
        CliResult freshFailure = await RunCliAsync(temp.Path, "compile", invalidPath, "--emit", "mir", "--out", freshFailurePath);

        Assert.Equal(1, staleFailure.ExitCode);
        Assert.Equal(1, freshFailure.ExitCode);
        Assert.DoesNotContain(temp.Path, staleFailure.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(staleHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(stalePath))));
        Assert.False(File.Exists(freshFailurePath));
    }

    [Fact]
    public async Task Failed_Tson_encoding_compilation_preserves_stale_output()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", """
            const $schema: string = "copeland://tests/cli-encoding";
            record Unsupported { values: number ! string; }
            function encode(value: Unsupported): string ! TsonEncodeError { return tsonEncode(value); }
            """);
        string outputPath = temp.WriteFile("output.g.js", "stale-output");

        CliResult result = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            "javascript",
            "--out",
            outputPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-TSON-ENCODE-0003", result.StdErr, StringComparison.Ordinal);
        Assert.Equal("stale-output", File.ReadAllText(outputPath));
    }

    [Theory]
    [InlineData("mir")]
    [InlineData("csharp")]
    [InlineData("javascript")]
    public async Task Rejected_Control_Flow_Source_Preserves_A_Fresh_Artifact(string emitTarget)
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", """
            function main(): number {
                let value: number = 0;
                for (; value < 2; value = value + 1) { }
                return value;
            }
            """);
        string outputPath = Path.Combine(temp.Path, "main.generated");

        CliResult accepted = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emitTarget, "--out", outputPath);
        Assert.Equal(0, accepted.ExitCode);
        string retainedArtifact = await File.ReadAllTextAsync(outputPath);

        temp.WriteFile("main.ts", "function main(): void { break; }");
        CliResult rejected = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emitTarget, "--out", outputPath);

        Assert.Equal(1, rejected.ExitCode);
        Assert.Contains("COPE-CFLOW-0001", rejected.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("wrote", rejected.StdOut, StringComparison.Ordinal);
        Assert.Equal(retainedArtifact, await File.ReadAllTextAsync(outputPath));
    }

    [Theory]
    [InlineData("mir")]
    [InlineData("csharp")]
    [InlineData("javascript")]
    public async Task Compile_resolves_Tson_asset_relative_to_source_for_every_emit_target(string emitTarget)
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", """
            const $schema: string = "copeland://tests/cli-assets";
            record Item { label: string; }
            enum State { Off, On(value: number), }
            record Settings { empty: number[]; values: number[]; items: Item[]; states: State[]; rows: number[][]; }
            function main(): Settings {
                const settings: Settings = tsonAsset("./settings.obj.ts");
                return settings;
            }
            """);
        temp.WriteFile("settings.obj.ts", """
            const $schema: string = "copeland://tests/cli-assets";
            record Item { label: string; }
            enum State { Off, On(value: number), }
            record Settings { empty: number[]; values: number[]; items: Item[]; states: State[]; rows: number[][]; }
            const $value: Settings = {
                empty: [],
                values: [1, 2],
                items: [{ label: "first" }],
                states: [State.Off, State.On(3)],
                rows: [[], [4]],
            };
            """);

        CliResult first = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emitTarget);
        CliResult second = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", emitTarget);

        Assert.Equal(0, first.ExitCode);
        Assert.DoesNotContain("tsonAsset", first.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("settings.obj.ts", first.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Tson", first.StdOut, StringComparison.Ordinal);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Failed_Tson_asset_compilation_preserves_stale_output()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", """
            const $schema: string = "copeland://tests/cli-assets";
            record Settings { value: number; }
            function main(): number {
                const settings: Settings = tsonAsset("./missing.tson");
                return settings.value;
            }
            """);
        string outputPath = temp.WriteFile("output.g.js", "stale-output");

        CliResult result = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            "javascript",
            "--out",
            outputPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-TSON-ASSET-0002", result.StdErr, StringComparison.Ordinal);
        Assert.Equal("stale-output", File.ReadAllText(outputPath));
    }

    [Fact]
    public async Task Invalid_array_asset_preserves_stale_output_without_deleting_unrelated_files()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("main.ts", """
            const $schema: string = "copeland://tests/cli-assets";
            record Settings { values: number[]; }
            function main(): Settings { const settings: Settings = tsonAsset("./settings.obj.ts"); return settings; }
            """);
        temp.WriteFile("settings.obj.ts", """
            const $schema: string = "copeland://tests/cli-assets";
            record Settings { values: number[]; }
            const $value: Settings = { values: ["wrong"], };
            """);
        string outputPath = temp.WriteFile("output.g.js", "stale-output");
        string unrelatedPath = temp.WriteFile("keep.txt", "preserve-me");

        CliResult result = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            "javascript",
            "--out",
            outputPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-TSON-0004", result.StdErr, StringComparison.Ordinal);
        Assert.Equal("stale-output", File.ReadAllText(outputPath));
        Assert.Equal("preserve-me", File.ReadAllText(unrelatedPath));
    }

    [Fact]
    public async Task EmitMirToStdout()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
function one(): number {
  return 1;
}
""");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "mir");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("module", result.StdOut);
        Assert.Contains("func one() -> number", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task EmitCSharpToStdout()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
function one(): number {
  return 1;
}
""");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("namespace Copeland.Generated", result.StdOut);
        Assert.Contains("public static double one()", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task EmitMirToFile()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");
        var outputPath = System.IO.Path.Combine(temp.Path, "output.cope");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "mir", "--out", outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("module", Normalize(File.ReadAllText(outputPath)));
        Assert.Contains($"wrote {outputPath}", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task EmitCSharpToFile()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");
        var outputPath = System.IO.Path.Combine(temp.Path, "output.g.cs");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp", "--out", outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("namespace Copeland.Generated", Normalize(File.ReadAllText(outputPath)));
    }

    [Fact]
    public async Task EmitJavaScriptToStdout()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"use strict\";", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("function one()", result.StdOut, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task JavaScriptSymbolicProfile_Emits_Executable_Symbolic_Artifact()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("input.ts", "record Point { x: number; } function main(): number { const point: Point = { x: 42 }; return point.x; }");
        string outputPath = Path.Combine(temp.Path, "output.sym.js");

        CliResult compilation = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            "javascript",
            "--javascript-profile",
            "symbolic",
            "--out",
            outputPath);

        Assert.Equal(0, compilation.ExitCode);
        string emitted = Normalize(await File.ReadAllTextAsync(outputPath));
        Assert.Contains("$录型甲", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("__cope_m3_", emitted, StringComparison.Ordinal);

        CliResult parsed = await RunExecutableAsync("node", temp.Path, "--check", outputPath);
        Assert.Equal(0, parsed.ExitCode);
        Assert.Equal(string.Empty, parsed.StdErr);

        await File.AppendAllTextAsync(outputPath, "console.log(main());\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CliResult executed = await RunExecutableAsync("node", temp.Path, outputPath);
        Assert.Equal(0, executed.ExitCode);
        Assert.Equal("42\n", executed.StdOut);
    }

    [Fact]
    public async Task JavaScriptProfile_Rejected_For_NonJavaScript_Emit_Target()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("input.ts", "function main(): number { return 42; }");

        CliResult result = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            "mir",
            "--javascript-profile",
            "symbolic");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("valid only with '--emit javascript'", result.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown")]
    public async Task Unsupported_JavaScriptProfile_Fails_Before_Artifact_Output(string profile)
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("input.ts", "function main(): number { return 42; }");
        string outputPath = Path.Combine(temp.Path, "output.js");

        CliResult result = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            "javascript",
            "--javascript-profile",
            profile,
            "--out",
            outputPath);

        Assert.Equal(2, result.ExitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Contains("Unsupported JavaScript profile", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JavaScriptProductionProfile_Emits_Stable_Trusted_Record_Representation()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("input.ts", "record Point { x: int; } function main(): int { const point: Point = { x: 42 }; return point.x; }");
        string outputPath = Path.Combine(temp.Path, "output.production.js");

        CliResult compilation = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            "javascript",
            "--javascript-profile",
            "production",
            "--out",
            outputPath);

        Assert.Equal(0, compilation.ExitCode);
        string emitted = Normalize(await File.ReadAllTextAsync(outputPath));
        Assert.Contains("$f0", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("new WeakSet()", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("Object.defineProperties", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmitJavaScriptEquality_Executes_In_Node()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
            function main(): boolean {
              const nan: number = 0.0 / 0.0;
              return nan != nan;
            }
            """);
        string outputPath = System.IO.Path.Combine(temp.Path, "output.g.js");

        CliResult compilation = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", outputPath);

        Assert.Equal(0, compilation.ExitCode);
        Assert.True(File.Exists(outputPath));
        string emitted = Normalize(File.ReadAllText(outputPath));
        Assert.Contains("nan !== nan", emitted, StringComparison.Ordinal);
        Assert.DoesNotMatch("(?<![=!])==(?!=)", emitted);
        Assert.DoesNotMatch("(?<!!)!=(?!=)", emitted);

        File.AppendAllText(outputPath, "console.log(main());\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CliResult execution = await RunExecutableAsync("node", temp.Path, outputPath);

        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("true\n", execution.StdOut);
        Assert.Equal(string.Empty, execution.StdErr);
    }

    [Fact]
    public async Task EmitJavaScriptPayloadEnumMatch_Executes_In_Node()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
            enum Choice {
              Empty,
              Pair(first: number, second: string),
            }

            function main(): string {
              const choice: Choice = Choice.Pair(1, "ordered");
              return match choice {
                Empty => "empty",
                Pair(first, second) => second,
              };
            }
            """);
        string outputPath = System.IO.Path.Combine(temp.Path, "output.g.js");

        CliResult compilation = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", outputPath);

        Assert.Equal(0, compilation.ExitCode);
        string emitted = Normalize(File.ReadAllText(outputPath));
        Assert.Contains("Object.create(null)", emitted, StringComparison.Ordinal);
        Assert.Contains("switch (__cope_m3_match_", emitted, StringComparison.Ordinal);

        File.AppendAllText(outputPath, "console.log(main());\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CliResult execution = await RunExecutableAsync("node", temp.Path, outputPath);

        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("ordered\n", execution.StdOut);
        Assert.Equal(string.Empty, execution.StdErr);
    }

    [Fact]
    public async Task EmitResultMir_CSharp_And_JavaScript()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("result.ts", """
            function result(): number ! string { return ok(4); }
            function main(): number {
              return match result() {
                ok(value) => value,
                err(error) => 0,
              };
            }
            """);
        string outputPath = System.IO.Path.Combine(temp.Path, "result.g.js");

        CliResult mir = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "mir");
        CliResult csharp = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp");
        CliResult javascript = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", outputPath);

        Assert.Equal(0, mir.ExitCode);
        Assert.Contains("result-match", mir.StdOut, StringComparison.Ordinal);
        Assert.Equal(0, csharp.ExitCode);
        Assert.Contains("CopeResult", csharp.StdOut, StringComparison.Ordinal);
        Assert.Equal(0, javascript.ExitCode);
        Assert.True(File.Exists(outputPath));

        File.AppendAllText(outputPath, "console.log(main());\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CliResult execution = await RunExecutableAsync("node", temp.Path, outputPath);

        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("4\n", execution.StdOut);
        Assert.Equal(string.Empty, execution.StdErr);
    }

    [Fact]
    public async Task JavaScriptArrayEmission_WritesOrdinaryArrayOutput()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function values(): number[] { return [1]; }");
        var outputPath = System.IO.Path.Combine(temp.Path, "output.g.js");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("return [1];", File.ReadAllText(outputPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Table_mir_csharp_and_javascript_emission_succeed()
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile("table.ts", "record table Values { value: number = [1, 2]; } function main(): number { const row: Values.Row = Values[1]!; return row.value; }");
        string mirPath = Path.Combine(temp.Path, "table.cope");
        string csharpPath = Path.Combine(temp.Path, "table.g.cs");
        string javaScriptPath = Path.Combine(temp.Path, "table.g.js");

        CliResult mir = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "mir", "--out", mirPath);
        CliResult csharp = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp", "--out", csharpPath);
        CliResult javaScript = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", javaScriptPath);

        Assert.Equal(0, mir.ExitCode);
        Assert.True(File.Exists(mirPath));
        Assert.Equal(0, csharp.ExitCode);
        Assert.True(File.Exists(csharpPath));
        string generated = await File.ReadAllTextAsync(csharpPath);
        Assert.Contains("__CopeTable_t1", generated, StringComparison.Ordinal);
        Assert.Equal(0, javaScript.ExitCode);
        Assert.True(File.Exists(javaScriptPath));
        await File.AppendAllTextAsync(javaScriptPath, "console.log(main());\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CliResult execution = await RunExecutableAsync("node", temp.Path, javaScriptPath);
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("2\n", execution.StdOut);
        Assert.Equal(string.Empty, execution.StdErr);
    }

    [Fact]
    public async Task Asset_backed_table_cli_emission_is_complete_deterministic_and_preserves_stale_output_on_failure()
    {
        using var temp = new TempDir();
        string source = """
            const $schema: string = "copeland://tests/cli-table-asset";
            record table Values from tsonAsset("./values.obj.ts") { value: number; }
            function main(): number { return Values.value[0]!; }
            """;
        string asset = """
            const $schema: string = "copeland://tests/cli-table-asset";
            record table Values { value: number = [42]; }
            const $value = Values;
            """;
        string sourcePath = temp.WriteFile("main.ts", source);
        temp.WriteFile("values.obj.ts", asset);
        string mirPath = Path.Combine(temp.Path, "main.cope");
        string csharpPath = Path.Combine(temp.Path, "main.g.cs");
        string javascriptPath = Path.Combine(temp.Path, "main.g.js");

        foreach ((string emit, string output) in new[]
                 {
                     ("mir", mirPath),
                     ("csharp", csharpPath),
                     ("javascript", javascriptPath),
                 })
        {
            CliResult first = await RunCliAsync(temp.Path, "compile", sourcePath, "--emit", emit, "--out", output);
            Assert.Equal(0, first.ExitCode);
            byte[] firstBytes = await File.ReadAllBytesAsync(output);
            CliResult second = await RunCliAsync(temp.Path, "compile", sourcePath, "--emit", emit, "--out", output);
            Assert.Equal(0, second.ExitCode);
            Assert.Equal(firstBytes, await File.ReadAllBytesAsync(output));
            string generated = await File.ReadAllTextAsync(output);
            Assert.DoesNotContain("values.obj.ts", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("tsonAsset", generated, StringComparison.Ordinal);
        }

        await File.AppendAllTextAsync(javascriptPath, "console.log(main());\n", new UTF8Encoding(false));
        CliResult execution = await RunExecutableAsync("node", temp.Path, javascriptPath);
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("42\n", execution.StdOut);

        string runnerProject = temp.WriteFile(
            "runner.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        temp.WriteFile(
            "Runner.cs",
            "Console.WriteLine(Copeland.Generated.CopelandModule.main());");
        CliResult csharpExecution = await RunExecutableAsync(
            "dotnet",
            temp.Path,
            "run",
            "--project",
            runnerProject);
        Assert.Equal(0, csharpExecution.ExitCode);
        Assert.Equal("42\n", csharpExecution.StdOut);

        byte[] staleBytes = await File.ReadAllBytesAsync(csharpPath);
        temp.WriteFile("values.obj.ts", asset.Replace("value: number", "value: string", StringComparison.Ordinal));
        CliResult staleFailure = await RunCliAsync(temp.Path, "compile", sourcePath, "--emit", "csharp", "--out", csharpPath);
        Assert.Equal(1, staleFailure.ExitCode);
        Assert.Contains("COPE-TSON-TABLE-0004", staleFailure.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain(temp.Path, staleFailure.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(staleBytes, await File.ReadAllBytesAsync(csharpPath));
    }

    [Fact]
    public async Task RecordMirCSharpAndJavaScriptSucceed_AndJavaScriptExecutes()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("record.ts", "record Point { x: number; } function main(): number { const point: Point = { x: 42 }; return point.x; }");
        var csharpPath = System.IO.Path.Combine(temp.Path, "record.g.cs");
        var javaScriptPath = System.IO.Path.Combine(temp.Path, "record.g.js");

        CliResult mir = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "mir");
        CliResult csharp = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp", "--out", csharpPath);
        CliResult javaScript = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "javascript", "--out", javaScriptPath);

        Assert.Equal(0, mir.ExitCode);
        Assert.Contains("record Point [r1]", mir.StdOut, StringComparison.Ordinal);
        Assert.Equal(0, csharp.ExitCode);
        Assert.True(File.Exists(csharpPath));
        string generatedCSharp = await File.ReadAllTextAsync(csharpPath);
        Assert.Contains("public sealed class __CopeRecord_r1", generatedCSharp, StringComparison.Ordinal);
        Assert.DoesNotContain("record __CopeRecord", generatedCSharp, StringComparison.Ordinal);
        Assert.Equal(0, javaScript.ExitCode);
        Assert.Equal(string.Empty, javaScript.StdErr);
        Assert.True(File.Exists(javaScriptPath));
        string generatedJavaScript = await File.ReadAllTextAsync(javaScriptPath);
        Assert.Contains("Symbol(\"r1\")", generatedJavaScript, StringComparison.Ordinal);
        Assert.DoesNotContain("COPE-JS-REC-0001", generatedJavaScript, StringComparison.Ordinal);

        await File.AppendAllTextAsync(javaScriptPath, "console.log(main());\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CliResult execution = await RunExecutableAsync("node", temp.Path, javaScriptPath);
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("42\n", execution.StdOut);
        Assert.Equal(string.Empty, execution.StdErr);
    }

    [Theory]
    [InlineData("mir", "record.cope")]
    [InlineData("csharp", "record.g.cs")]
    [InlineData("javascript", "record.g.js")]
    public async Task Rejected_Record_Source_Does_Not_Overwrite_Or_Masquerade_An_Earlier_Artifact(
        string emitTarget,
        string outputName)
    {
        using var temp = new TempDir();
        string inputPath = temp.WriteFile(
            "record.ts",
            "record Point { x: number; } function main(): Point { return { x: 42 }; }");
        string outputPath = System.IO.Path.Combine(temp.Path, outputName);

        CliResult accepted = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            emitTarget,
            "--out",
            outputPath);
        Assert.Equal(0, accepted.ExitCode);
        string retainedArtifact = await File.ReadAllTextAsync(outputPath);

        temp.WriteFile(
            "record.ts",
            "record Point { x: number; } function main(): Point { return { y: 42 }; }");
        CliResult rejected = await RunCliAsync(
            temp.Path,
            "compile",
            inputPath,
            "--emit",
            emitTarget,
            "--out",
            outputPath);

        Assert.Equal(1, rejected.ExitCode);
        Assert.Contains("COPE-REC-0007", rejected.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("wrote", rejected.StdOut, StringComparison.Ordinal);
        Assert.Equal(retainedArtifact, await File.ReadAllTextAsync(outputPath));
    }

    [Fact]
    public async Task InvalidSourceExitsOneAndDoesNotWriteOutput()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
function main(): number {
  const x: number = "bad";
  return x;
}
""");
        var outputPath = System.IO.Path.Combine(temp.Path, "output.g.cs");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp", "--out", outputPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-TYPE", result.StdErr);
        Assert.False(File.Exists(outputPath));
        Assert.DoesNotContain("namespace Copeland.Generated", result.StdOut);
    }


    [Fact]
    public async Task Ternary_Profile_Ban_ExitsOne_With_Diagnostic()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", """
function value(flag: boolean): number {
  return flag ? 1 : 2;
}
""");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "csharp");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("COPE-PROFILE-0007", result.StdErr);
    }

    [Fact]
    public async Task MissingEmitExitsTwo()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");

        var result = await RunCliAsync(temp.Path, "compile", inputPath);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage:", result.StdErr);
        Assert.Contains("COPE-CLI-0001", result.StdErr);
    }

    [Fact]
    public async Task UnknownEmitExitsTwo()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.ts", "function one(): number { return 1; }");

        var result = await RunCliAsync(temp.Path, "compile", inputPath, "--emit", "wasm");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("COPE-CLI-0002", result.StdErr);
    }

    [Fact]
    public async Task MissingInputFileExitsThree()
    {
        using var temp = new TempDir();
        var missingPath = System.IO.Path.Combine(temp.Path, "missing.ts");

        var result = await RunCliAsync(temp.Path, "compile", missingPath, "--emit", "mir");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("COPE-CLI-0008", result.StdErr);
    }

    [Fact]
    public async Task MarkdownParseMirJsonToStdout()
    {
        using var temp = new TempDir();
        var inputPath = temp.WriteFile("input.md", "# Heading");

        var result = await RunCliAsync(temp.Path, "markdown", "parse", inputPath, "--emit", "mir", "--format", "json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"kind\": \"DocumentMir\"", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("\"Heading\"", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatabaseBuildEmitsRepeatableExternalSegmentsAndGeneratedApi()
    {
        using var temp = new TempDir();
        string schemaPath = temp.WriteFile(
            "schema.ts",
            """
            const $schema: string = "copeland://experimental/cli-events/v1";
            export record Event {
                tenant: string;
                year: int;
                value: number;
            }
            """);
        string definitionPath = temp.WriteFile(
            "index.tsx",
            """
            export default defineDatabase(
                <Database name="Events">
                    <Index field="tenant">
                        <Index field="year">
                            <Table type={Event} />
                        </Index>
                    </Index>
                </Database>
            );
            """);
        string inputPath = temp.WriteFile(
            "events.json",
            """
            [
              { "tenant": "a", "year": 2025, "value": 1000 },
              { "tenant": "a", "year": 2026, "value": 1.5 },
              { "tenant": "a", "year": 2026, "value": 2.5 },
              { "tenant": "b", "year": 2026, "value": 10000 }
            ]
            """);
        string outputPath = Path.Combine(temp.Path, "database");
        string generatedPath = Path.Combine(temp.Path, "generated", "EventsDatabase.g.cs");
        string[] arguments =
        [
            "database",
            "build",
            "--schema",
            schemaPath,
            "--definition",
            definitionPath,
            "--input",
            inputPath,
            "--output",
            outputPath,
            "--generated-source",
            generatedPath,
        ];

        CliResult first = await RunCliAsync(temp.Path, arguments);
        byte[] firstRoot = await File.ReadAllBytesAsync(Path.Combine(outputPath, "root.index"));
        string[] firstLeaves = Directory.GetFiles(Path.Combine(outputPath, "leaves"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllBytes)
            .Select(Convert.ToHexString)
            .ToArray();
        string firstSource = await File.ReadAllTextAsync(generatedPath);
        CliResult second = await RunCliAsync(temp.Path, arguments);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Contains("Built 4 rows into 3 leaves", first.StdOut, StringComparison.Ordinal);
        Assert.Equal(firstRoot, await File.ReadAllBytesAsync(Path.Combine(outputPath, "root.index")));
        Assert.Equal(
            firstLeaves,
            Directory.GetFiles(Path.Combine(outputPath, "leaves"))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllBytes)
                .Select(Convert.ToHexString));
        Assert.Equal(firstSource, await File.ReadAllTextAsync(generatedPath));
        Assert.Contains("public sealed class EventsDatabase", firstSource, StringComparison.Ordinal);
        Assert.Contains("public double SumValue(string tenant, int year)", firstSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TableTools_ProjectBoundColumnsAsRowsAndApplyAtomicCompilerValidatedEdits()
    {
        using var temp = new TempDir();
        string sourcePath = temp.WriteFile(
            "Workbook.ts",
            """
            enum Department { Engineering, Sales, }

            export record table Scores {
                employeeId: int = [1, 2, 3];
                name: string = ["Alice", "Bob", "Carol"];
                score: number = [95.0, 81.5, 91.0];
            }

            export record table Employees {
                id: int = [1, 2, 3];
                name: string = ["Alice", "Bob", "Carol"];
                department: Department = [Department.Engineering, Department.Sales, Department.Engineering];
            }

            export function bobScore(): number { return Scores.score[1]!; }
            """);
        string repoRoot = GetRepoRoot();
        string taskProjectPath = Path.Combine(repoRoot, "src", "Copeland", "Copeland.TS.MSBuild", "Copeland.TS.MSBuild.csproj");
        string taskTargetsPath = Path.Combine(repoRoot, "src", "Copeland", "Copeland.TS.MSBuild", "build", "Copeland.TS.Sdk.targets");
        string taskAssemblyPath = Path.Combine(repoRoot, "src", "Copeland", "Copeland.TS.MSBuild", "bin", "Debug", "net10.0", "Copeland.TS.MSBuild.dll");
        string workbookProjectPath = temp.WriteFile(
            "Workbook.csproj",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>TableToolProof</AssemblyName>
                <RootNamespace>TableToolProof</RootNamespace>
                <CopelandTaskAssembly>{{taskAssemblyPath}}</CopelandTaskAssembly>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{taskProjectPath}}" ReferenceOutputAssembly="false" />
                <CopelandCompile Include="Workbook.ts" />
                <Compile Remove="Program.cs" />
              </ItemGroup>
              <Import Project="{{taskTargetsPath}}" />
            </Project>
            """);
        string consumerProjectPath = temp.WriteFile(
            "Consumer.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="Workbook.csproj" /></ItemGroup>
            </Project>
            """);
        temp.WriteFile(
            "Program.cs",
            "using System; using Workbook = TableToolProof.Copeland.Workbook; Console.WriteLine(Workbook.bobScore().ToString(System.Globalization.CultureInfo.InvariantCulture));");

        CliResult list = await RunCliAsync(temp.Path, "table", "list", sourcePath, "--format", "json");
        CliResult schema = await RunCliAsync(temp.Path, "table", "schema", sourcePath, "Scores", "--format", "json");
        CliResult rows = await RunCliAsync(temp.Path, "table", "rows", sourcePath, "Scores", "--format", "json");

        Assert.Equal(0, list.ExitCode);
        Assert.Equal(0, schema.ExitCode);
        Assert.Equal(0, rows.ExitCode);
        using (JsonDocument listJson = JsonDocument.Parse(list.StdOut))
        {
            Assert.Equal("Scores", listJson.RootElement.GetProperty("tables")[0].GetProperty("name").GetString());
            Assert.Equal(3, listJson.RootElement.GetProperty("tables")[0].GetProperty("rowCount").GetInt32());
        }

        using (JsonDocument schemaJson = JsonDocument.Parse(schema.StdOut))
        {
            Assert.Equal("number", schemaJson.RootElement.GetProperty("columns")[2].GetProperty("type").GetString());
        }

        using (JsonDocument rowsJson = JsonDocument.Parse(rows.StdOut))
        {
            Assert.Equal("Bob", rowsJson.RootElement.GetProperty("rows")[1].GetProperty("values").GetProperty("name").GetString());
        }

        byte[] original = await File.ReadAllBytesAsync(sourcePath);
        CliResult invalidValue = await RunCliAsync(temp.Path, "table", "set", sourcePath, "Scores", "--row", "1", "--column", "score", "--value", "not-a-number", "--format", "json");
        CliResult incomplete = await RunCliAsync(temp.Path, "table", "add-row", sourcePath, "Employees", "--json", "{\"id\":4}", "--format", "json");
        Assert.Equal(1, invalidValue.ExitCode);
        Assert.Equal(1, incomplete.ExitCode);
        Assert.Equal(original, await File.ReadAllBytesAsync(sourcePath));
        Assert.Contains("COPE-TABLE-TOOL-0010", invalidValue.StdOut, StringComparison.Ordinal);
        Assert.Contains("COPE-TABLE-TOOL-0009", incomplete.StdOut, StringComparison.Ordinal);
        using (JsonDocument invalidValueJson = JsonDocument.Parse(invalidValue.StdOut))
        {
            Assert.Equal("table.set", invalidValueJson.RootElement.GetProperty("command").GetString());
        }

        CliResult set = await RunCliAsync(temp.Path, "table", "set", sourcePath, "Scores", "--row", "1", "--column", "score", "--value", "84.0", "--format", "json");
        CliResult stringSet = await RunCliAsync(temp.Path, "table", "set", sourcePath, "Scores", "--row", "1", "--column", "name", "--value", "Bob, Jr.", "--format", "json");
        CliResult add = await RunCliAsync(temp.Path, "table", "add-row", sourcePath, "Employees", "--json", "{\"id\":4,\"name\":\"Dana\",\"department\":\"Engineering\"}", "--format", "json");
        CliResult delete = await RunCliAsync(temp.Path, "table", "delete-row", sourcePath, "Employees", "--row", "3", "--format", "json");
        Assert.Equal(0, set.ExitCode);
        Assert.Equal(0, stringSet.ExitCode);
        Assert.Equal(0, add.ExitCode);
        Assert.Equal(0, delete.ExitCode);
        string edited = await File.ReadAllTextAsync(sourcePath);
        Assert.Contains("score: number = [95.0, 84.0, 91.0];", edited, StringComparison.Ordinal);
        Assert.Contains("Department.Engineering];", edited, StringComparison.Ordinal);

        string csvPath = Path.Combine(temp.Path, "Scores.csv");
        CliResult export = await RunCliAsync(temp.Path, "table", "export", sourcePath, "Scores", "--format", "csv", "--output", csvPath, "--result-format", "json");
        string firstCsv = await File.ReadAllTextAsync(csvPath);
        CliResult import = await RunCliAsync(temp.Path, "table", "import", sourcePath, "Scores", "--format", "csv", "--input", csvPath, "--replace", "--result-format", "json");
        CliResult exportAgain = await RunCliAsync(temp.Path, "table", "export", sourcePath, "Scores", "--format", "csv", "--output", csvPath);
        Assert.Equal(0, export.ExitCode);
        Assert.Equal(0, import.ExitCode);
        Assert.Equal(firstCsv, await File.ReadAllTextAsync(csvPath));
        Assert.Contains("\"Bob, Jr.\"", firstCsv, StringComparison.Ordinal);
        Assert.Contains("\"command\": \"table.export\"", export.StdOut, StringComparison.Ordinal);
        Assert.Contains("\"command\": \"table.import\"", import.StdOut, StringComparison.Ordinal);
        string reimported = await File.ReadAllTextAsync(sourcePath);
        Assert.Contains("score: number = [95.0, 84.0, 91.0];", reimported, StringComparison.Ordinal);

        CliResult validation = await RunCliAsync(temp.Path, "table", "validate", sourcePath, "--format", "json");
        CliResult csharp = await RunCliAsync(temp.Path, "compile", sourcePath, "--emit", "csharp");
        Assert.Equal(0, validation.ExitCode);
        Assert.Equal(0, csharp.ExitCode);
        Assert.Contains("84", csharp.StdOut, StringComparison.Ordinal);

        // This integration test builds a temporary consumer while the solution
        // test runner also loads the MSBuild task assembly. Disable reusable
        // build servers so the timeout cleanup cannot leave a worker holding
        // Copeland.TS.dll for the next project.
        CliResult consumerBuild = await RunExecutableAsync("dotnet", temp.Path, "build", consumerProjectPath, "--disable-build-servers");
        CliResult consumer = await RunExecutableAsync("dotnet", temp.Path, "run", "--project", consumerProjectPath, "--no-build");
        Assert.True(consumerBuild.ExitCode == 0, consumerBuild.StdOut + consumerBuild.StdErr);
        Assert.True(consumer.ExitCode == 0, consumer.StdOut + consumer.StdErr);
        Assert.Equal("84\n", consumer.StdOut);
    }

    [Fact]
    public async Task Workspace_sync_partitions_sources_generates_deterministic_artifacts_and_serves_json_owner_queries()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src", "legacy"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "src", "copeland"));
        temp.WriteFile("src/legacy/Legacy.ts", "export const legacy: string = 'legacy';");
        temp.WriteFile("src/copeland/Domain.ts", "export function Domain(): string { return 'domain'; }");
        temp.WriteFile("App.csproj", "<Project />");
        string manifest = temp.WriteFile("tsconfig.tsx", """
            export default defineTypeScriptWorkspace({
                tsc: {
                    include: ["src\\legacy\\**"],
                    compilerOptions: { strict: true, target: "ES2024", module: "ESNext" }
                },
                tscl: { project: "./App.csproj", include: ["src/copeland/**"] }
            });
            """);

        CliResult firstSync = await RunCliAsync(temp.Path, "workspace", "sync", "--workspace", manifest, "--format", "json");
        CliResult secondSync = await RunCliAsync(temp.Path, "workspace", "sync", "--workspace", manifest, "--format", "json");
        CliResult owner = await RunCliAsync(temp.Path, "workspace", "owner", "src/copeland/Domain.ts", "--workspace", manifest, "--format", "json");

        Assert.Equal(0, firstSync.ExitCode);
        Assert.True(JsonDocument.Parse(firstSync.StdOut).RootElement.GetProperty("changed").GetBoolean());
        Assert.Equal(0, secondSync.ExitCode);
        Assert.False(JsonDocument.Parse(secondSync.StdOut).RootElement.GetProperty("changed").GetBoolean());
        Assert.Equal(0, owner.ExitCode);
        Assert.Equal("tscl", JsonDocument.Parse(owner.StdOut).RootElement.GetProperty("owner").GetString());

        string generatedDirectory = Path.Combine(temp.Path, "obj", "copeland", "workspace");
        string tscConfig = await File.ReadAllTextAsync(Path.Combine(generatedDirectory, "tsconfig.generated.json"));
        string props = await File.ReadAllTextAsync(Path.Combine(generatedDirectory, "tscl-files.generated.props"));
        string ownership = await File.ReadAllTextAsync(Path.Combine(generatedDirectory, "editor-ownership.generated.json"));
        Assert.Contains("Legacy.ts", tscConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("Domain.ts", tscConfig, StringComparison.Ordinal);
        Assert.Contains("Domain.ts", props, StringComparison.Ordinal);
        Assert.Contains("\"owner\": \"tsc\"", ownership, StringComparison.Ordinal);
        Assert.Contains("\"owner\": \"tscl\"", ownership, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workspace_validate_rejects_overlap_and_strict_unowned_sources()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src", "shared"));
        temp.WriteFile("src/shared/Model.ts", "export const model = 1;");
        temp.WriteFile("App.csproj", "<Project />");
        string overlapManifest = temp.WriteFile("tsconfig.tsx", """
            export default defineTypeScriptWorkspace({
                tsc: { include: ["src/**"], compilerOptions: { strict: true } },
                tscl: { project: "App.csproj", include: ["src/shared/**"] }
            });
            """);

        CliResult overlap = await RunCliAsync(temp.Path, "workspace", "validate", "--workspace", overlapManifest, "--format", "json");

        Assert.Equal(1, overlap.ExitCode);
        Assert.Contains("COPE-WORKSPACE-0021", overlap.StdOut, StringComparison.Ordinal);

        string unownedManifest = temp.WriteFile("tsconfig.tsx", """
            export default defineTypeScriptWorkspace({
                tsc: { include: ["src/other/**"], compilerOptions: { strict: true } }
            });
            """);
        CliResult unowned = await RunCliAsync(temp.Path, "workspace", "validate", "--workspace", unownedManifest, "--format", "json");

        Assert.Equal(1, unowned.ExitCode);
        Assert.Contains("COPE-WORKSPACE-0022", unowned.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workspace_supports_all_tsc_and_copeland_only_adoption_shapes_with_folder_transfer()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        temp.WriteFile("src/Only.ts", "export function Only(): string { return 'only'; }");
        temp.WriteFile("App.csproj", "<Project />");
        string manifest = temp.WriteFile("tsconfig.tsx", """
            export default defineTypeScriptWorkspace({
                tsc: { include: ["src/**"], compilerOptions: { strict: true, target: "ES2024" } }
            });
            """);

        CliResult allTsc = await RunCliAsync(temp.Path, "workspace", "sync", "--workspace", manifest, "--format", "json");
        Assert.Equal(0, allTsc.ExitCode);
        string allTscConfig = await File.ReadAllTextAsync(Path.Combine(temp.Path, "obj", "copeland", "workspace", "tsconfig.generated.json"));
        Assert.Contains("Only.ts", allTscConfig, StringComparison.Ordinal);

        manifest = temp.WriteFile("tsconfig.tsx", """
            export default defineTypeScriptWorkspace({
                tscl: { project: "App.csproj", include: ["src/**"] }
            });
            """);
        CliResult copelandOnly = await RunCliAsync(temp.Path, "workspace", "sync", "--workspace", manifest, "--format", "json");
        Assert.Equal(0, copelandOnly.ExitCode);
        string props = await File.ReadAllTextAsync(Path.Combine(temp.Path, "obj", "copeland", "workspace", "tscl-files.generated.props"));
        Assert.Contains("Only.ts", props, StringComparison.Ordinal);
        string transferredConfig = await File.ReadAllTextAsync(Path.Combine(temp.Path, "obj", "copeland", "workspace", "tsconfig.generated.json"));
        Assert.DoesNotContain("Only.ts", transferredConfig, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Layout_inspection_is_a_read_only_projected_table_surface()
    {
        using var temp = new TempDir();
        string sourcePath = temp.WriteFile("DialogScene.ts", """
            layout DialogScene<0px, 0px> {
                width: 320px;
                height: 180px;
                overlay root {
                    slot dialog { frame: { x: 20px, y: 20px, width: 260px, height: 120px }; z: 1; }
                }
            }
            """);

        CliResult list = await RunCliAsync(temp.Path, "table", "list", "--source", sourcePath, "--format", "json");
        CliResult rows = await RunCliAsync(temp.Path, "table", "rows", "layout::Boxes", "--source", sourcePath, "--format", "json");
        CliResult mutation = await RunCliAsync(temp.Path, "table", "set", "layout::Boxes", "--source", sourcePath, "--row", "0", "--column", "kind", "--value", "slot", "--format", "json");
        CliResult inspect = await RunCliAsync(temp.Path, "layout", "inspect", "DialogScene", "--source", sourcePath, "--json");

        Assert.Equal(0, list.ExitCode);
        Assert.Equal(0, rows.ExitCode);
        Assert.Equal(1, mutation.ExitCode);
        Assert.Equal(0, inspect.ExitCode);
        Assert.Contains("layout::Boxes", list.StdOut, StringComparison.Ordinal);
        Assert.Contains("DialogScene.root.dialog", rows.StdOut, StringComparison.Ordinal);
        Assert.Contains("COPE-TABLE-PROJECTED-0001", mutation.StdOut, StringComparison.Ordinal);
        Assert.Contains("DialogScene.root.dialog", inspect.StdOut, StringComparison.Ordinal);
        Assert.Contains("sourceKind", inspect.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Projected_layout_tools_use_the_materialized_manifest_context()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        Directory.CreateDirectory(Path.Combine(temp.Path, ".tspack", "build-manifests"));
        string sourcePath = temp.WriteFile("src/Site.ts", "layout Site<0px, 0px> { width: 320px; height: 180px; slot hero { frame: { x: 0px, y: 0px, width: 320px, height: 180px }; } }");
        string manifestPath = temp.WriteFile("manifest.tsx", "export default manifest({});");
        temp.WriteFile(".tspack/build-manifests/site-browser.request.json", $$"""
            {
              "projectRoot": "{{temp.Path.Replace("\\", "\\\\")}}",
              "sources": [
                { "logicalPath": "src/Site.ts", "path": "{{sourcePath.Replace("\\", "\\\\")}}" }
              ],
              "javascriptRuntime": "browser",
              "npmContracts": []
            }
            """);

        CliResult byProject = await RunCliAsync(temp.Path, "table", "list", "--project", manifestPath, "--format", "json");
        CliResult bySource = await RunCliAsync(temp.Path, "table", "rows", "layout::Boxes", "--source", sourcePath, "--format", "json");
        CliResult inspection = await RunCliAsync(temp.Path, "layout", "inspect", "Site", "--project", manifestPath, "--json");

        Assert.Equal(0, byProject.ExitCode);
        Assert.Equal(0, bySource.ExitCode);
        Assert.Equal(0, inspection.ExitCode);
        Assert.Contains("graphFingerprint", byProject.StdOut, StringComparison.Ordinal);
        Assert.Contains("graphFingerprint", bySource.StdOut, StringComparison.Ordinal);
        Assert.Contains("graphFingerprint", inspection.StdOut, StringComparison.Ordinal);
        Assert.Contains("Site.hero", inspection.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Table_query_executes_typed_derived_relations_without_writing_source()
    {
        using var temp = new TempDir();
        string sourcePath = temp.WriteFile("Workbook.ts", """
            export record table Categories { key id: int = [10, 20]; name: string = ["Coffee", "Equipment"]; }
            export record table Products { key id: int = [1, 2, 3]; reference categoryId: int -> Categories.id = [10, 20, 10]; name: string = ["Beans", "Kettle", "Filter"]; }
            export record table Prices { key reference productId: int -> Products.id = [1, 2, 3]; retail: number = [18.5, 42.0, 16.25]; cost: number = [9.25, 21.0, 7.5]; }
            export record table ProductCatalog = derive Products as product
                join Categories as category through product.categoryId
                join Prices as price through price.productId {
                productName: string = product.name;
                categoryName: string = category.name;
                retail: number = price.retail;
                margin: number = price.retail - price.cost;
            }
            """);
        string queryPath = temp.WriteFile("query.json", """
            {"where":{"operator":"greaterThan","left":{"column":"retail"},"right":{"number":15.0}},"select":[{"column":"productName"},{"column":"retail","as":"price"}],"orderBy":[{"column":"retail","direction":"descending"}],"skip":1,"take":1}
            """);
        byte[] before = await File.ReadAllBytesAsync(sourcePath);

        CliResult text = await RunCliAsync(temp.Path, "table", "query", sourcePath, "ProductCatalog", "--where", "retail > 15.0", "--select", "productName, categoryName, retail", "--order-by", "retail desc", "--take", "2");
        CliResult authored = await RunCliAsync(temp.Path, "table", "query", sourcePath, "Products", "--select", "name", "--order-by", "name asc", "--take", "1", "--format", "json");
        CliResult structured = await RunCliAsync(temp.Path, "table", "query", sourcePath, "ProductCatalog", "--query-json", queryPath, "--format", "json");
        CliResult csv = await RunCliAsync(temp.Path, "table", "query", sourcePath, "ProductCatalog", "--select", "productName, retail", "--order-by", "retail desc", "--take", "1", "--format", "csv");
        CliResult explain = await RunCliAsync(temp.Path, "table", "query", sourcePath, "ProductCatalog", "--where", "retail > 15.0", "--explain", "--format", "json");
        CliResult invalid = await RunCliAsync(temp.Path, "table", "query", sourcePath, "ProductCatalog", "--where", "missing > 0", "--format", "json");

        Assert.Equal(0, text.ExitCode);
        Assert.Contains("Kettle", text.StdOut, StringComparison.Ordinal);
        Assert.Equal(0, authored.ExitCode);
        Assert.Contains("\"name\": \"Beans\"", authored.StdOut, StringComparison.Ordinal);
        Assert.Equal(0, structured.ExitCode);
        Assert.Contains("\"command\": \"table.query\"", structured.StdOut, StringComparison.Ordinal);
        Assert.Contains("\"productName\": \"Beans\"", structured.StdOut, StringComparison.Ordinal);
        Assert.Equal(0, csv.ExitCode);
        Assert.Equal("productName,retail\nKettle,42\n", csv.StdOut);
        Assert.Equal(0, explain.ExitCode);
        Assert.Contains("csharp-relation-plan", explain.StdOut, StringComparison.Ordinal);
        Assert.Equal(1, invalid.ExitCode);
        Assert.Contains("COPE-TABLE-QUERY-0003", invalid.StdOut, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(sourcePath));
    }

    [Fact]
    public async Task Table_query_aggregates_and_groups_typed_derived_relations_without_post_processing_rows()
    {
        using var temp = new TempDir();
        string sourcePath = temp.WriteFile("Workbook.ts", """
            export record table Categories { key id: int = [10, 20]; name: string = ["Coffee", "Equipment"]; }
            export record table Products { key id: int = [1, 2, 3]; reference categoryId: int -> Categories.id = [10, 20, 10]; name: string = ["Beans", "Kettle", "Filter"]; }
            export record table Prices { key reference productId: int -> Products.id = [1, 2, 3]; retail: number = [18.5, 42.0, 16.25]; }
            export record table ProductCatalog = derive Products as product
                join Categories as category through product.categoryId
                join Prices as price through price.productId {
                productName: string = product.name;
                categoryName: string = category.name;
                retail: number = price.retail;
            }
            """);
        string queryPath = temp.WriteFile("aggregate.json", """
            {"groupBy":[{"column":"categoryName"}],"aggregates":[{"function":"sum","input":{"column":"retail"},"as":"totalRetail"},{"function":"count","as":"productCount"}],"orderBy":[{"column":"totalRetail","direction":"descending"}]}
            """);
        byte[] before = await File.ReadAllBytesAsync(sourcePath);

        CliResult overall = await RunCliAsync(temp.Path, "table", "query", sourcePath, "ProductCatalog", "--aggregate", "count() as productCount, count(retail) as pricedProductCount, sum(retail) as totalRetail, average(retail) as averageRetail, min(retail) as minimumRetail, max(retail) as maximumRetail", "--format", "json");
        CliResult defaultGroups = await RunCliAsync(temp.Path, "table", "query", sourcePath, "ProductCatalog", "--group-by", "categoryName", "--aggregate", "sum(retail) as totalRetail", "--format", "csv");
        CliResult grouped = await RunCliAsync(temp.Path, "table", "query", sourcePath, "ProductCatalog", "--group-by", "categoryName", "--aggregate", "sum(retail) as totalRetail, count() as productCount", "--order-by", "totalRetail desc", "--take", "1", "--format", "csv");
        CliResult structured = await RunCliAsync(temp.Path, "table", "query", sourcePath, "ProductCatalog", "--query-json", queryPath, "--format", "json");
        CliResult empty = await RunCliAsync(temp.Path, "table", "query", sourcePath, "ProductCatalog", "--where", "retail > 100.0", "--aggregate", "count() as productCount, sum(retail) as totalRetail", "--format", "json");
        CliResult invalidEmpty = await RunCliAsync(temp.Path, "table", "query", sourcePath, "ProductCatalog", "--where", "retail > 100.0", "--aggregate", "average(retail) as averageRetail", "--format", "json");
        CliResult explain = await RunCliAsync(temp.Path, "table", "query", sourcePath, "ProductCatalog", "--group-by", "categoryName", "--aggregate", "sum(retail) as totalRetail", "--explain", "--format", "json");

        Assert.Equal(0, overall.ExitCode);
        Assert.Contains("\"totalRetail\": 76.75", overall.StdOut, StringComparison.Ordinal);
        Assert.Contains("\"pricedProductCount\": 3", overall.StdOut, StringComparison.Ordinal);
        Assert.Contains("\"minimumRetail\": 16.25", overall.StdOut, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"aggregate\"", overall.StdOut, StringComparison.Ordinal);
        Assert.Equal(0, grouped.ExitCode);
        Assert.Equal("categoryName,totalRetail,productCount\nEquipment,42,1\n", grouped.StdOut);
        Assert.Equal(0, defaultGroups.ExitCode);
        Assert.Equal("categoryName,totalRetail\nCoffee,34.75\nEquipment,42\n", defaultGroups.StdOut);
        Assert.Equal(0, structured.ExitCode);
        Assert.Contains("\"categoryName\": \"Equipment\"", structured.StdOut, StringComparison.Ordinal);
        Assert.Contains("\"sourceTable\": \"Products\"", structured.StdOut, StringComparison.Ordinal);
        Assert.Equal(0, empty.ExitCode);
        Assert.Contains("\"productCount\": 0", empty.StdOut, StringComparison.Ordinal);
        Assert.Contains("\"totalRetail\": 0", empty.StdOut, StringComparison.Ordinal);
        Assert.Equal(1, invalidEmpty.ExitCode);
        Assert.Contains("COPE-TABLE-QUERY-0027", invalidEmpty.StdOut, StringComparison.Ordinal);
        Assert.Equal(0, explain.ExitCode);
        Assert.Contains("\"groupBy\": [", explain.StdOut, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(sourcePath));
    }

    private static async Task<CliResult> RunCliAsync(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(GetCliAssemblyPath());
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Copeland CLI process.");
        process.StandardInput.Close();

        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync();

            string timedOutStdOut = Normalize(await stdOutTask);
            string timedOutStdErr = Normalize(await stdErrTask);
            throw new TimeoutException(BuildTimeoutMessage(args, timedOutStdOut, timedOutStdErr));
        }
        finally
        {
            if (!process.HasExited)
            {
                KillProcessTree(process);
            }
        }

        string stdOut = Normalize(await stdOutTask);
        string stdErr = Normalize(await stdErrTask);

        return new CliResult(process.ExitCode, stdOut, stdErr);
    }

    private static async Task<CliResult> RunExecutableAsync(string fileName, string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        process.StandardInput.Close();
        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync();
            throw new TimeoutException(BuildTimeoutMessage(args, Normalize(await stdOutTask), Normalize(await stdErrTask)));
        }

        return new CliResult(process.ExitCode, Normalize(await stdOutTask), Normalize(await stdErrTask));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
    }

    private static string BuildTimeoutMessage(string[] args, string stdOut, string stdErr)
    {
        var message = new StringBuilder();
        message.AppendLine($"Copeland CLI exceeded the 60 second test timeout. Arguments: {string.Join(' ', args)}");
        message.AppendLine("stdout:");
        message.AppendLine(stdOut);
        message.AppendLine("stderr:");
        message.AppendLine(stdErr);
        return message.ToString();
    }

    private static string GetCliAssemblyPath()
    {
        var repoRoot = GetRepoRoot();
        var testOutputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        string targetFramework = testOutputDirectory.Name;
        string configuration = testOutputDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the test build configuration.");
        string cliAssemblyPath = System.IO.Path.Combine(
            repoRoot,
            "src",
            "Copeland",
            "Copeland.Cli",
            "bin",
            configuration,
            targetFramework,
            "Copeland.Cli.dll");

        if (!File.Exists(cliAssemblyPath))
        {
            throw new FileNotFoundException(
                "The Copeland CLI must be built before its process contract tests run.",
                cliAssemblyPath);
        }

        return cliAssemblyPath;
    }

    private static string GetRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "copeland-cli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string WriteFile(string relativePath, string text)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            File.WriteAllText(fullPath, text);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
