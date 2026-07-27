using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class NuGetPackageContractTests
{
    [Fact]
    public void Clr_binary_package_import_lowers_to_direct_clr_call()
    {
        var contract = CreateMathContract();

        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            import { Abs } from "example/math";
            export function Calculate(): int { return Abs(-2); }
            """, new CopelandCompilationOptions
        {
            PackageContracts = [contract],
            ClrReferences = [new CopelandClrReference(typeof(Math).Assembly.Location)],
            SourcePath = "Consumer.ts",
        });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("clr-call", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("System.Math.Abs", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_package_module_ownership_is_not_selected_by_item_order()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            import { Abs } from "example/math";
            export function Calculate(): int { return Abs(-2); }
            """, new CopelandCompilationOptions
        {
            PackageContracts = [CreateMathContract("First"), CreateMathContract("Second")],
            SourcePath = "Consumer.ts",
        });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-PACKAGE-0006");
    }

    [Fact]
    public void Native_and_npm_contract_ownership_is_ambiguous()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            import { Abs } from "example/math";
            export function Calculate(): int { return 0; }
            """, new CopelandCompilationOptions
        {
            PackageContracts = [CreateMathContract()],
            NpmPackages = [new CopelandNpmPackageContract("example/math", "1.0.0", [])],
            SourcePath = "Consumer.ts",
        });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-PACKAGE-0006");
    }

    [Fact]
    public void Node_target_rejects_clr_only_package_before_emission()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            import { Abs } from "example/math";
            export function Calculate(): int { return Abs(-2); }
            """, new CopelandCompilationOptions
        {
            PackageContracts = [CreateMathContract()],
            PackageBackend = CopelandPackageBackend.JavaScriptNode,
            SourcePath = "Consumer.ts",
        });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-PACKAGE-0007");
    }

    [Fact]
    public void Missing_named_export_is_reported_at_the_authored_import()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            import { Missing } from "example/math";
            export function Calculate(): int { return 0; }
            """, new CopelandCompilationOptions
        {
            PackageContracts = [CreateMathContract()],
            SourcePath = "Consumer.ts",
        });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-PACKAGE-0009");
    }

    [Fact]
    public void Contract_binary_mismatch_is_reported_before_csharp_emission()
    {
        CopelandPackageContract contract = CreateMathContractWithMethod("Missing");
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            import { Abs } from "example/math";
            export function Calculate(): int { return Abs(-2); }
            """, new CopelandCompilationOptions
        {
            PackageContracts = [contract],
            ClrReferences = [new CopelandClrReference(typeof(Math).Assembly.Location)],
            SourcePath = "Consumer.ts",
        });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-PACKAGE-0013");
    }

    [Fact]
    public void Contract_reader_rejects_unsupported_schema_version()
    {
        string path = Path.Combine(Path.GetTempPath(), "copeland-package-contract-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """{ "schemaVersion": 2 }""");

            bool read = CopelandPackageContractReader.TryRead(path, out _, out string? error);

            Assert.False(read);
            Assert.StartsWith("COPE-PACKAGE-0003", error, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static CopelandPackageContract CreateMathContract(string packageId = "Example.Math")
        => CreateMathContractWithMethod("Abs", packageId);

    private static CopelandPackageContract CreateMathContractWithMethod(string method, string packageId = "Example.Math")
    {
        string assembly = typeof(Math).Assembly.GetName().Name!;
        var export = new CopelandPackageExportContract(
            "Abs",
            "function",
            [new CopelandPackageParameterContract("value", "int")],
            "int",
            "System.Math",
            method);
        var module = new CopelandPackageModuleContract(
            "example/math",
            packageId + "/example/math",
            [export],
            new CopelandClrBinaryRealization(assembly));
        return new CopelandPackageContract("contract.v1.json", packageId, "1.0", [module]);
    }
}
