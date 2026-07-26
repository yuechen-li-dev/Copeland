using Copeland.TS.Compiler;
using Copeland.TS.Manifest;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ManifestProfileTests
{
    [Fact]
    public void Loads_Real_Root_Manifest_Into_Immutable_Manifest_Model()
    {
        string source = ReadFixture("valid-root.manifest.tsx");
        string root = Path.Combine(Path.GetTempPath(), "Copeland.Manifest.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "manifest.tsx"), source);
            ManifestProjectLoadResult result = CopelandProject.LoadRootManifest(root);

            Assert.True(result.Success, Describe(result.Diagnostics));
            CopelandManifest manifest = Assert.IsType<CopelandManifest>(result.Manifest);
            Assert.Equal("sample", manifest.Workspace.Name);
            Assert.Equal("nodejs", manifest.Workspace.Runtime);
            ManifestPackage package = Assert.Single(manifest.Packages);
            Assert.Equal("@sample/app", package.Name);
            ManifestRunTarget target = Assert.Single(package.RunTargets);
            Assert.Equal("serve", target.Name);
            Assert.Equal(["server/main.js", "--port", "4173"], target.Command);
            Assert.Equal("node", target.Runtime);
            Assert.Equal("package", target.WorkingDirectory);
            Assert.Single(manifest.CompatFiles);
            Assert.Equal("sample/@sample/app/serve", Assert.Single(manifest.DeploymentBindings).LogicalIdentity);
            ManifestSidecarBinding sidecar = Assert.Single(manifest.Sidecars);
            Assert.Equal("node-transport", sidecar.LogicalBindingId);
            Assert.Equal("sample/@sample/app/serve", sidecar.RunTargetIdentity);
            Assert.True(sidecar.IsDefault);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("unknown-element.manifest.tsx", "COPE-MANIFEST-0011")]
    [InlineData("unknown-field-and-mixed.manifest.tsx", "COPE-MANIFEST-0021")]
    [InlineData("duplicate-and-nesting.manifest.tsx", "COPE-MANIFEST-0030")]
    [InlineData("invalid-types.manifest.tsx", "COPE-MANIFEST-0029")]
    [InlineData("restricted-expression.manifest.tsx", "COPE-MANIFEST-0026")]
    public void Rejects_Manifest_Schema_And_Restricted_Expression_Violations(string fixture, string diagnosticId)
    {
        ManifestBindingResult result = BindFixture(fixture, ManifestBindingContext.RootProject);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("manifest.tsx", Path.GetFileName(diagnostic.SourcePath)));
    }

    [Fact]
    public void Reports_Manifest_Diagnostics_At_The_Offending_TsXml_Field()
    {
        string source = ReadFixture("unknown-field-and-mixed.manifest.tsx");
        ManifestBindingResult result = BindFixture("unknown-field-and-mixed.manifest.tsx", ManifestBindingContext.RootProject);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "COPE-MANIFEST-0021");

        Assert.Equal(source.IndexOf("unexpected", StringComparison.Ordinal), diagnostic.Position);
        Assert.True(diagnostic.Length > 0);
    }

    [Fact]
    public void Dependency_Manifest_Cannot_Acquire_Root_Deployment_Authority()
    {
        ManifestBindingResult result = BindFixture("dependency-run-target.manifest.tsx", ManifestBindingContext.DependencyManifest);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-MANIFEST-0018");
    }

    [Fact]
    public void Sidecars_Reject_Launch_Fields_Unknown_Targets_And_Duplicate_Defaults()
    {
        ManifestBindingResult result = ManifestBinder.Bind(
            SyntaxTree.Parse("""
                import { define } from "tspack/manifest";
                export default define(<Workspace name="sample"><Package name="app" version="1" kind="app"><RunTargets rows={[{ name: "node", runtime: "node", command: ["sidecar.js"] }]} /></Package><Sidecars rows={[{ id: "one", runTarget: "sample/app/missing", default: true, command: ["nope"] }, { id: "two", runTarget: "sample/app/node", default: true }]} /></Workspace>);
                """, "manifest.tsx"),
            "C:/project",
            "manifest.tsx",
            ManifestBindingContext.RootProject);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-MANIFEST-0032");
    }

    [Fact]
    public void Ordinary_Tsx_Remains_Neutral_Without_Manifest_Context()
    {
        CopelandCompilation compilation = CopelandCompiler.Compile(
            "const view = <Workspace name=\"sample\" />;",
            new CopelandCompilationOptions
            {
                SourcePath = "ordinary.tsx",
                TargetStage = CopelandCompilationStage.Bound,
            });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSXML-0101");
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Id.StartsWith("COPE-MANIFEST", StringComparison.Ordinal));
    }

    [Fact]
    public void Root_Loader_Requires_The_Exact_Root_Manifest_Name()
    {
        string root = Path.Combine(Path.GetTempPath(), "Copeland.Manifest.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "other.tsx"), "const value = 1;");
            ManifestProjectLoadResult result = CopelandProject.LoadRootManifest(root);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-MANIFEST-0001");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ManifestBindingResult BindFixture(string fixture, ManifestBindingContext context)
    {
        string source = ReadFixture(fixture);
        SyntaxTree tree = SyntaxTree.Parse(source, "manifest.tsx");
        return ManifestBinder.Bind(tree, "C:/project", "manifest.tsx", context);
    }

    private static string ReadFixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Manifest", name));

    private static string Describe(IEnumerable<Diagnostics.Diagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));
}
