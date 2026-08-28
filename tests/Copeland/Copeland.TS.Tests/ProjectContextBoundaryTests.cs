using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ProjectContextBoundaryTests
{
    [Fact]
    public void Managed_context_uses_Copeland_config_for_internal_source_ownership()
    {
        using var project = new TemporaryProject();
        string copelandSource = project.Write("src/copeland/Main.ts", "export function Main(): string { return \"ok\"; }");
        string tscSource = project.Write("src/tsc/browser.ts", "export const browser = true;");
        project.WriteConfig();
        string descriptorPath = project.WriteDescriptor([copelandSource, tscSource]);

        CopelandProjectContext context = CopelandProjectContext.LoadResolvedContext(descriptorPath);

        CopelandProjectSource source = Assert.Single(context.Sources);
        Assert.Equal("src/copeland/Main.ts", source.LogicalPath);
    }

    [Fact]
    public void Standalone_and_managed_modes_converge_on_semantic_context()
    {
        using var project = new TemporaryProject();
        string copelandSource = project.Write("src/copeland/Main.ts", "export function Main(): string { return \"ok\"; }");
        project.Write("manifest.tsx", MinimalManifest("[]"));
        project.WriteConfig();
        string descriptorPath = project.WriteDescriptor([copelandSource]);

        CopelandProjectContext standalone = CopelandProjectContext.LoadStandalone(project.Path);
        CopelandProjectContext managed = CopelandProjectContext.LoadResolvedContext(descriptorPath);

        Assert.Equal(standalone.Fingerprint, managed.Fingerprint);
        Assert.Equal(standalone.Sources.Select(source => source.LogicalPath), managed.Sources.Select(source => source.LogicalPath));
    }

    [Fact]
    public void Repeated_resolved_context_load_has_bounded_startup_overhead()
    {
        using var project = new TemporaryProject();
        string source = project.Write("src/copeland/Main.ts", "export function Main(): string { return \"ok\"; }");
        project.WriteConfig();
        string descriptorPath = project.WriteDescriptor([source]);

        var stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < 50; index += 1)
        {
            _ = CopelandProjectContext.LoadResolvedContext(descriptorPath);
        }
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"50 context loads took {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
    }

    [Fact]
    public void Standalone_mode_reports_missing_local_package_without_realizing_it()
    {
        using var project = new TemporaryProject();
        project.Write("src/copeland/Main.ts", "export function Main(): string { return \"ok\"; }");
        project.Write("manifest.tsx", MinimalManifest("[deps.react]", "const deps = defineDeps({ react: dep(npm(\"react\", \"^19\")) });"));
        project.WriteConfig();

        CopelandProjectContextException exception = Assert.Throws<CopelandProjectContextException>(
            () => CopelandProjectContext.LoadStandalone(project.Path));

        Assert.Equal("COPE-PROJECT-0015", exception.Code);
        Assert.Contains("use TSPack to resolve/materialize", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(System.IO.Path.Combine(project.Path, "node_modules")));
    }

    [Fact]
    public void Managed_context_uses_resolved_binding_over_conflicting_payload_or_local_world()
    {
        using var project = new TemporaryProject();
        string source = project.Write("src/copeland/Main.ts", "export function Main(): string { return \"ok\"; }");
        project.WriteConfig();
        string selectedPath = System.IO.Path.Combine(project.Path, "store", "react-19.1.0");
        string temptingPath = System.IO.Path.Combine(project.Path, "node_modules", "react");
        Directory.CreateDirectory(selectedPath);
        Directory.CreateDirectory(temptingPath);
        string descriptorPath = project.WriteDescriptor([source], (selectedPath, temptingPath));

        CopelandProjectContext context = CopelandProjectContext.LoadResolvedContext(descriptorPath);

        CopelandProjectContextNpmContract binding = Assert.Single(context.Descriptor.NpmContracts);
        Assert.Equal("19.1.0", binding.Version);
        Assert.Equal(selectedPath, binding.MaterializationPath);
    }

    [Fact]
    public void Descriptor_protocol_rejects_unknown_future_version()
    {
        CompilerTargetDescriptor descriptor = ValidDescriptor() with { SchemaVersion = 2 };

        CopelandProjectContextException exception = Assert.Throws<CopelandProjectContextException>(
            () => CompilerTargetDescriptorProtocol.Validate(descriptor));

        Assert.Equal("COPE-PROJECT-0009", exception.Code);
    }

    [Fact]
    public void Descriptor_protocol_requires_Copeland_payload()
    {
        CompilerTargetDescriptor descriptor = ValidDescriptor() with { CompilerPayload = null };

        CopelandProjectContextException exception = Assert.Throws<CopelandProjectContextException>(
            () => CompilerTargetDescriptorProtocol.Validate(descriptor));

        Assert.Equal("COPE-PROJECT-0010", exception.Code);
    }

    [Fact]
    public void Descriptor_protocol_accepts_unknown_additive_fields()
    {
        using var project = new TemporaryProject();
        string source = project.Write("src/copeland/Main.ts", "export function Main(): string { return \"ok\"; }");
        project.WriteConfig();
        string descriptorPath = project.WriteDescriptor([source]);
        JsonObject descriptor = JsonNode.Parse(File.ReadAllText(descriptorPath))!.AsObject();
        descriptor["futureField"] = new JsonObject { ["value"] = 1 };
        File.WriteAllText(descriptorPath, descriptor.ToJsonString());

        CopelandProjectContext context = CopelandProjectContext.LoadResolvedContext(descriptorPath);

        Assert.Single(context.Sources);
    }

    [Theory]
    [InlineData("projectRoot", "COPE-PROJECT-0016")]
    [InlineData("runtime", "COPE-PROJECT-0017")]
    public void Managed_context_rejects_payload_conflicts_with_generic_authority(
        string conflictingField,
        string expectedCode)
    {
        using var project = new TemporaryProject();
        string source = project.Write("src/copeland/Main.ts", "export function Main(): string { return \"ok\"; }");
        project.WriteConfig();
        string descriptorPath = project.WriteDescriptor([source]);
        JsonObject descriptor = JsonNode.Parse(File.ReadAllText(descriptorPath))!.AsObject();
        JsonObject payload = descriptor["compilerPayload"]!["data"]!.AsObject();
        if (conflictingField == "projectRoot")
        {
            payload["projectRoot"] = System.IO.Path.Combine(project.Path, "other");
        }
        else
        {
            payload["javaScriptRuntime"] = "browser";
        }
        File.WriteAllText(descriptorPath, descriptor.ToJsonString());

        CopelandProjectContextException exception = Assert.Throws<CopelandProjectContextException>(
            () => CopelandProjectContext.LoadResolvedContext(descriptorPath));

        Assert.Equal(expectedCode, exception.Code);
    }

    private static CompilerTargetDescriptor ValidDescriptor()
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        return new CompilerTargetDescriptor
        {
            SchemaVersion = 1,
            ProjectRoot = "C:/workspace",
            Target = new CompilerTargetIdentity { Package = "app", Name = "main" },
            Language = new CompilerIdentity { Id = "copeland-ts" },
            Compiler = new VersionedCompilerIdentity { Id = "tscl", Version = "1.0.0" },
            Sources = [new CompilerTargetSource { LogicalPath = "src/Main.ts", Path = "src/Main.ts" }],
            CompilerPayload = new CompilerTargetPayload { Kind = "copeland-v1", SchemaVersion = 1, Data = document.RootElement.Clone() },
        };
    }

    private static string MinimalManifest(string dependencyValues, string declarations = "")
        => string.Join(Environment.NewLine, new[]
        {
            "import { Package, Targets, Workspace, define, defineDeps, dep, npm } from \"tspack/manifest\";",
            declarations,
            "export default define(",
            "  <Workspace name=\"sample\" runtime=\"nodejs\">",
            $"    <Package name=\"app\" version=\"1.0.0\" kind=\"app\" dependencies={{{{ values: {dependencyValues} }}}}>",
            "      <Targets rows={[{ name: \"main\", entry: \"src/copeland/Main.ts\", runtime: \"dist/main.js\", deps: [], peers: [] }]} />",
            "    </Package>",
            "  </Workspace>,",
            ");",
        });

    private sealed class TemporaryProject : IDisposable
    {
        public TemporaryProject()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Copeland.M71.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string relativePath, string contents)
        {
            string fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, contents);
            return fullPath;
        }

        public void WriteConfig()
        {
            Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Write("tsconfig.tsx", """
                import { defineTypeScriptWorkspace } from "copeland/workspace";
                export default defineTypeScriptWorkspace({
                    ownership: "partial",
                    tsc: { include: ["src/tsc/**"] },
                    tscl: { project: "./App.csproj", include: ["src/copeland/**"] }
                });
                """);
        }

        public string WriteDescriptor(
            IReadOnlyList<string> sources,
            (string SelectedPath, string TemptingPath)? conflictingPackage = null)
        {
            object[] npmContracts = conflictingPackage is null
                ? []
                : [new
                {
                    packageName = "react",
                    version = "18.0.0",
                    materializationPath = conflictingPackage.Value.TemptingPath,
                    materialized = true,
                    exports = Array.Empty<object>(),
                    components = Array.Empty<object>(),
                }];
            object[] packages = conflictingPackage is null
                ? []
                : [new
                {
                    semanticIdentity = "npm:react",
                    version = "19.1.0",
                    materializationPath = conflictingPackage.Value.SelectedPath,
                    materializationName = "react",
                    localName = "react-alias",
                    role = "runtime",
                }];
            var payload = new
            {
                projectRoot = Path,
                sources = sources.Select(source => new { logicalPath = Relative(source), path = source }),
                entry = new { module = Relative(sources[0]), export = "Main" },
                javaScriptRuntime = "node",
                javaScriptProfile = "production",
                outputDirectory = System.IO.Path.Combine(Path, "dist"),
                entryOutputPath = "main.js",
                npmContracts,
            };
            var descriptor = new
            {
                schemaVersion = 1,
                projectRoot = Path,
                target = new { package = "app", name = "main" },
                language = new { id = "copeland-ts" },
                compiler = new { id = "tscl", version = "1.0.0" },
                tool = new { source = "path", name = "copeland", version = "1.0.0", path = "tscl" },
                compilerConfig = new { kind = "file", path = "tsconfig.tsx", fingerprint = "test" },
                sources = sources.Select(source => new { logicalPath = Relative(source), path = source, fingerprint = "test" }),
                packages,
                runtime = new { family = "javascript", name = "node" },
                outputs = new[] { new { kind = "javaScript", path = "dist/main.js" } },
                capabilities = new[] { "parse", "typeCheck", "emitJavaScript" },
                compilerPayload = new { kind = "copeland-v1", schemaVersion = 1, data = payload },
            };
            return Write(".tspack/build-manifests/app-main.request.json", JsonSerializer.Serialize(descriptor));
        }

        private string Relative(string path)
            => System.IO.Path.GetRelativePath(Path, path).Replace('\\', '/');

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
