using Copeland.TS.Compiler;
using Copeland.TS.Workspace;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class SourceFileProfileTests
{
    [Theory]
    [InlineData("Legacy", "tsc")]
    [InlineData("Copeland", "tscl")]
    [InlineData("JavaScript", "tscl")]
    [InlineData("Dotnet", "tscl")]
    [InlineData("Dotnet-NativeAOT", "tscl")]
    public void Headers_resolve_without_parsing_the_language_body(string name, string owner)
    {
        string source = $"// license\r\nexport type CopelandProfile = \"{name}\";\r\nconst dynamic: any = undefined;";
        SourceFileProfile profile = SourceFileProfiles.Read(source);
        Assert.Empty(profile.Diagnostics);
        Assert.Equal(name, profile.Name);
        Assert.Equal(owner, profile.Owner);
        Assert.Equal(source.Length, profile.Mask(source).Length);
        Assert.EndsWith("const dynamic: any = undefined;", profile.Mask(source));
    }

    [Theory]
    [InlineData("export type CopelandProfile = \"Javascript\";", "COPE-PROFILE-0001")]
    [InlineData("export type CopelandProfile = {};", "COPE-PROFILE-0001")]
    [InlineData("export type CopelandProfile = \"JavaScript\" | \"Dotnet\";", "COPE-PROFILE-0001")]
    [InlineData("export type CopelandProfile = \"JavaScript\"; type CopelandProfile = \"Dotnet\";", "COPE-PROFILE-0002")]
    public void Invalid_headers_are_primary_diagnostics(string source, string expected)
    {
        var compilation = CopelandCompiler.CompileToMir(source);
        Assert.False(compilation.Success);
        Assert.Single(compilation.Diagnostics);
        Assert.Equal(expected, compilation.Diagnostics[0].Id);
    }

    [Fact]
    public void Copeland_headers_preserve_module_graph_and_source_positions()
    {
        const string source = "export type CopelandProfile = \"Copeland\";\nexport function answer(): int { return 42; }";
        var compilation = CopelandProjectCompiler.CompileToMir([
            new CopelandProjectSource("Answer.ts", "Answer.ts", source),
            new CopelandProjectSource("Use.ts", "Use.ts", "export type CopelandProfile = \"Dotnet\";\nimport { answer } from \"./Answer\"; export function run(): int { return answer(); }")
        ]);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.DoesNotContain(compilation.Modules.SelectMany(module => module.Exports), name => name == "CopelandProfile");
        var invalid = CopelandCompiler.CompileToMir(source.Replace("return 42", "return missing"));
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Position == source.Replace("return 42", "return missing").IndexOf("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Target_checks_apply_to_every_project_source_and_native_runtime()
    {
        const string source = "export type CopelandProfile = \"JavaScript\";\nexport function run(): int { return 1; }";
        var compilation = CopelandProjectCompiler.CompileToMir(
            [new CopelandProjectSource("Main.ts", "Main.ts", source)],
            new CopelandCompilationOptions { BackendTarget = CopelandBackendTarget.Create("csharp", "ryujit") });
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-PROFILE-0004");
        SourceFileProfile native = SourceFileProfiles.Read("export type CopelandProfile = 'Dotnet-NativeAOT';");
        Assert.NotNull(native.CheckTarget("csharp", "ryujit"));
        Assert.Null(native.CheckTarget("csharp", "nativeaot"));
        Assert.Null(native.CheckTarget("csharp"));
        SourceFileProfile shared = SourceFileProfiles.Read("export type CopelandProfile = 'Copeland';");
        Assert.Null(shared.CheckTarget("javascript", "browser"));
        Assert.Null(shared.CheckTarget("csharp", "ryujit"));
    }

    [Fact]
    public void Legacy_is_rejected_by_the_copeland_frontend_without_ts_parse_cascades()
    {
        var compilation = CopelandCompiler.CompileToMir("export type CopelandProfile = \"Legacy\";\nconst value: any = undefined;");
        Assert.Single(compilation.Diagnostics);
        Assert.Equal("COPE-PROFILE-0003", compilation.Diagnostics[0].Id);
    }

    [Fact]
    public void Comments_and_strings_do_not_claim_a_profile_and_bom_shebang_remain_readable()
    {
        Assert.Null(SourceFileProfiles.Read("// type CopelandProfile = 'Legacy';\nexport function run(): int { return 1; }").Name);
        Assert.Null(SourceFileProfiles.Read("const text: string = 'type CopelandProfile = Legacy';").Name);
        var compilation = CopelandCompiler.CompileToMir("\uFEFF#!/usr/bin/env tscl\n/* license */\nexport type CopelandProfile = 'Copeland';\nfunction run(): int { return 1; }");
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
    }
}
