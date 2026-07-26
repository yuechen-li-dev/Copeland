using Xunit;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Tson;

namespace Copeland.Cli.Tests;

public sealed class CliIntegrationTests
{
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
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(corpus, fileName)), firstBytes);
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
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(corpus, fileName)), firstBytes);
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
    [InlineData("release")]
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

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
        message.AppendLine($"Copeland CLI exceeded the 10 second test timeout. Arguments: {string.Join(' ', args)}");
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
