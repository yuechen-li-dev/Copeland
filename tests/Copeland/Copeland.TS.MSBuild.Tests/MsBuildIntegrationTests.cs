using System.Diagnostics;
using Xunit;

namespace Copeland.TS.MSBuild.Tests;

public sealed class MsBuildIntegrationTests
{
    [Fact]
    public void Tsconfig_owned_sources_compile_without_explicit_msbuild_items()
    {
        using var fixture = new TemporaryProject();
        string taskAssembly = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.MSBuild.dll"));
        string props = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.Sdk.props"));
        string targets = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.Sdk.targets"));
        fixture.Write("Demo/Demo.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{{props}}" />
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Demo</RootNamespace>
                <CopelandTaskAssembly>{{taskAssembly}}</CopelandTaskAssembly>
              </PropertyGroup>
              <Import Project="{{targets}}" />
            </Project>
            """);
        fixture.Write("Demo/tsconfig.tsx", """
            import { defineTypeScriptWorkspace } from "copeland/workspace";

            export default defineTypeScriptWorkspace({
                ownership: "strict",
                tscl: {
                    project: "./Demo.csproj",
                    include: ["src/copeland/**"]
                }
            });
            """);
        fixture.Write("Demo/src/copeland/Greeting.ts", """
            export function Message(): string {
                return "owned by tsconfig.tsx";
            }
            """);
        fixture.Write("Demo/Program.cs", """
            using Demo.Copeland;
            System.Console.WriteLine(Greeting.Message());
            """);

        fixture.Run("Demo", "restore");
        fixture.Run("Demo", "build", "--no-restore");
        fixture.Run("Demo", "run", "--no-build").AssertOutput("owned by tsconfig.tsx");
    }

    [Fact]
    public void Normal_Project_Builds_Runs_Publishes_And_Cleans_Copeland_Intermediate_Source()
    {
        using var fixture = new TemporaryProject();
        fixture.Write("Library/Library.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        fixture.Write("Library/Prefixes.cs", """
            namespace Referenced;
            public static class Prefixes
            {
                public static string Add(string value) => "ref:" + value;
            }
            """);
        fixture.Write("Demo/Demo.csproj", CreateProjectFile());
        fixture.Write("Demo/Program.cs", """
            using Demo.Copeland;
            System.Console.WriteLine(Greeting.Message("Copeland"));
            System.Console.WriteLine(Feature.Make("Copeland"));
            """);
        fixture.Write("Demo/Greeting.ts", """
            using System;
            function Message(name: string): string {
                return String.Concat("Hello, ", name);
            }
            """);
        fixture.Write("Demo/Feature.ts", """
            using Referenced;
            function Make(name: string): string {
                return Prefixes.Add(name);
            }
            """);
        fixture.Write("Demo/Package.ts", """
            using Tomlyn;
            function HasPackageMetadata(): boolean {
                return TomlSerializer.IsReflectionEnabledByDefault;
            }
            """);
        fixture.Write("Demo/frontend.ts", "this is not Copeland syntax and must remain ignored;");

        fixture.Run("Demo", "restore");
        fixture.Run("Demo", "build", "--no-restore");
        fixture.Run("Demo", "run", "--no-build").AssertOutput("Hello, Copeland", "ref:Copeland");

        fixture.Write("Demo.Tests/Demo.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsPackable>false</IsPackable></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
                <ProjectReference Include="..\\Demo\\Demo.csproj" />
              </ItemGroup>
            </Project>
            """);
        fixture.Write("Demo.Tests/GreetingTests.cs", """
            using Demo.Copeland;
            using Xunit;
            public sealed class GreetingTests
            {
                [Fact]
                public void Calls_Copeland_From_Normal_Test_Project()
                {
                    Assert.Equal("Hello, test", Greeting.Message("test"));
                }
            }
            """);
        fixture.Run("Demo.Tests", "restore");
        fixture.Run("Demo.Tests", "test", "--no-restore");

        string generated = Path.Combine(fixture.Root, "Demo", "obj", "Debug", "net10.0", "Copeland", "Greeting.g.cs");
        string mir = Path.Combine(Path.GetDirectoryName(generated)!, "Greeting.cope");
        Assert.True(File.Exists(generated));
        Assert.True(File.Exists(mir));
        Assert.DoesNotContain("frontend", Directory.EnumerateFiles(Path.GetDirectoryName(generated)!, "*.g.cs").Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);

        DateTime initialWrite = File.GetLastWriteTimeUtc(generated);
        fixture.Run("Demo", "build", "--no-restore");
        Assert.Equal(initialWrite, File.GetLastWriteTimeUtc(generated));

        fixture.Write("Demo/Greeting.ts", """
            using System;
            function Message(name: string): string {
                return String.Concat("Updated, ", name);
            }
            """);
        fixture.Run("Demo", "build", "--no-restore");
        fixture.Run("Demo", "run", "--no-build").AssertOutput("Updated, Copeland", "ref:Copeland");

        fixture.Delete("Demo/Feature.ts");
        fixture.Write("Demo/Demo.csproj", CreateProjectFile(includeFeature: false));
        fixture.Write("Demo/Program.cs", """
            using Demo.Copeland;
            System.Console.WriteLine(Greeting.Message("Copeland"));
            """);
        fixture.Run("Demo", "build", "--no-restore");
        Assert.False(File.Exists(Path.Combine(fixture.Root, "Demo", "obj", "Debug", "net10.0", "Copeland", "Feature.g.cs")));

        fixture.Run("Demo", "build", "-c", "Release", "--no-restore");
        fixture.Run("Demo", "publish", "-c", "Release", "--no-restore", "-o", "publish");
        fixture.Run(Path.Combine("Demo", "publish"), "Demo.dll").AssertOutput("Updated, Copeland");
    }

    [Fact]
    public void Same_project_tsxtest_files_run_through_xunit_in_a_separate_test_assembly()
    {
        using var fixture = new TemporaryProject();
        string taskAssembly = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.MSBuild.dll"));
        string props = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.Sdk.props"));
        string targets = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.Sdk.targets"));
        fixture.Write("Demo/Demo.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{{props}}" />
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Demo</RootNamespace>
                <CopelandTaskAssembly>{{taskAssembly}}</CopelandTaskAssembly>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <CopelandCompile Include="Calculator.ts" />
              </ItemGroup>
              <Import Project="{{targets}}" />
            </Project>
            """);
        fixture.Write("Demo/Program.cs", "System.Console.WriteLine(\"production\");");
        fixture.Write("Demo/Calculator.ts", "export function Add(left: number, right: number): number { return left + right; }");
        fixture.Write("Demo/Calculator.tsxtest", """
            using Xunit;

            import { Add } from "./Calculator";

            [Fact]
            export function Add_returns_sum(): void {
                Assert.Equal(42, Add(20, 22));
            }

            [Theory]
            [InlineData(1, 2, 3)]
            [InlineData(10, 20, 30)]
            export function Add_returns_expected(left: number, right: number, expected: number): void {
                Assert.Equal(expected, Add(left, right));
            }
            """);

        string cleanPackageCache = Path.Combine(fixture.Root, "nuget-packages");
        fixture.RunWithNugetPackages("Demo", cleanPackageCache, "restore");
        fixture.RunWithNugetPackages("Demo", cleanPackageCache, "build", "--no-restore");
        ProcessResult result = fixture.RunWithNugetPackages("Demo", cleanPackageCache, "test", "--no-restore");

        string productionOutput = Path.Combine(fixture.Root, "Demo", "bin", "Debug", "net10.0", "Demo.dll");
        string[] testOutputs = Directory.GetFiles(
            Path.Combine(fixture.Root, "Demo"),
            "Demo.CopelandTests.dll",
            SearchOption.AllDirectories);
        Assert.True(File.Exists(productionOutput));
        Assert.NotEmpty(testOutputs);
        Assert.True(result.Output.Contains("Passed!", StringComparison.OrdinalIgnoreCase), result.Output);

        fixture.Run("Demo", "publish", "--no-restore", "-o", "publish");
        Assert.False(File.Exists(Path.Combine(fixture.Root, "Demo", "publish", "Demo.CopelandTests.dll")));
    }

    [Fact]
    public void Dedicated_xunit_project_discovers_tsxtest_without_a_copeland_property()
    {
        using var fixture = new TemporaryProject();
        string taskAssembly = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.MSBuild.dll"));
        string props = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.Sdk.props"));
        string targets = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.Sdk.targets"));
        fixture.Write("Demo/Demo.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        fixture.Write("Demo/Calculator.cs", "namespace Demo; public static class Calculator { public static double Add(double left, double right) => left + right; }");
        fixture.Write("Demo.Tests/Demo.Tests.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{{props}}" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <CopelandTaskAssembly>{{taskAssembly}}</CopelandTaskAssembly>
                <CopelandSameProjectTestHost>false</CopelandSameProjectTestHost>
                <CopelandCompileTestsInProject>true</CopelandCompileTestsInProject>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Demo\Demo.csproj" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" PrivateAssets="all" />
              </ItemGroup>
              <Import Project="{{targets}}" />
            </Project>
            """);
        fixture.Write("Demo.Tests/Calculator.tsxtest", """
            using Xunit;
            using Demo;

            [Fact]
            export function Copeland_calls_referenced_csharp(): void {
                Assert.True(Calculator.Add(20, 22) == 42);
            }
            """);
        fixture.Write("Demo.Tests/CSharpTests.cs", """
            using Xunit;
            public sealed class CSharpTests { [Fact] public void Csharp_test_coexists() => Assert.True(true); }
            """);

        fixture.Run("Demo.Tests", "restore");
        ProcessResult result = fixture.Run("Demo.Tests", "test", "--no-restore");
        Assert.True(result.Output.Contains("Passed!", StringComparison.OrdinalIgnoreCase), result.Output);
        Assert.Contains("Demo.Tests.dll", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Copeland_Diagnostics_Are_Reported_Against_The_Authored_Source()
    {
        using var fixture = new TemporaryProject();
        fixture.Write("Demo/Demo.csproj", CreateProjectFile(includeFeature: false, includeProjectReference: false, includePackage: false));
        fixture.Write("Demo/Program.cs", "System.Console.WriteLine(\"unreachable\");");
        fixture.Write("Demo/Greeting.ts", "function Message(name: string): string { return missing; }");

        fixture.Run("Demo", "restore");
        ProcessResult result = fixture.RunExpectingFailure("Demo", "build", "--no-restore");

        Assert.True(result.Output.Contains("Greeting.ts", StringComparison.OrdinalIgnoreCase), result.Output);
        Assert.True(result.Output.Contains("COPE-", StringComparison.Ordinal), result.Output);
        Assert.DoesNotContain("Greeting.g.cs", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Multiple_Copeland_failures_remain_the_visible_MSBuild_root_cause()
    {
        using var fixture = new TemporaryProject();
        fixture.Write("Demo/Demo.csproj", CreateProjectFile(includeProjectReference: false, includePackage: false));
        fixture.Write("Demo/Program.cs", "System.Console.WriteLine(\"unreachable\");");
        fixture.Write("Demo/Greeting.ts", "function Message(): string { return missingGreeting; }");
        fixture.Write("Demo/Feature.ts", "function Feature(): string { return missingFeature; }");

        fixture.Run("Demo", "restore");
        ProcessResult result = fixture.RunExpectingFailure("Demo", "build", "--no-restore");

        Assert.Contains("COPE-BIND-0001", result.Output, StringComparison.Ordinal);
        Assert.Contains("Greeting.ts", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Feature.ts", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CS0246", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Independent_Copeland_Files_Scope_Private_Record_Carriers_Per_Module()
    {
        using var fixture = new TemporaryProject();
        fixture.Write("Demo/Demo.csproj", CreateProjectFile(includeProjectReference: false, includePackage: false));
        fixture.Write("Demo/Program.cs", """
            using Demo.Copeland;
            System.Console.WriteLine(Greeting.Message("greeting"));
            System.Console.WriteLine(Feature.Make("feature"));
            """);
        fixture.Write("Demo/Greeting.ts", """
            record GreetingValue { value: string; }
            function Message(value: string): string {
                const greeting: GreetingValue = { value, };
                return greeting.value;
            }
            """);
        fixture.Write("Demo/Feature.ts", """
            record FeatureValue { value: string; }
            function Make(value: string): string {
                const feature: FeatureValue = { value, };
                return feature.value;
            }
            """);

        fixture.Run("Demo", "restore");
        fixture.Run("Demo", "build", "--no-restore");
        fixture.Run("Demo", "run", "--no-build").AssertOutput("greeting", "feature");

        string generatedDirectory = Path.Combine(fixture.Root, "Demo", "obj", "Debug", "net10.0", "Copeland");
        string greeting = File.ReadAllText(Path.Combine(generatedDirectory, "Greeting.g.cs"));
        string feature = File.ReadAllText(Path.Combine(generatedDirectory, "Feature.g.cs"));
        Assert.Contains("__CopeRecord_Greeting_r1", greeting, StringComparison.Ordinal);
        Assert.Contains("__CopeRecord_Feature_r1", feature, StringComparison.Ordinal);
    }

    [Fact]
    public void Copeland_Binds_Authored_CSharp_Declarations_In_The_Same_Project()
    {
        using var fixture = new TemporaryProject();
        fixture.Write("Demo/Demo.csproj", CreateMixedProjectFile());
        string names = """
            namespace Demo;

            public static class Names
            {
                public static string Normalize(string value) => value.Trim().ToUpperInvariant();
                private static string Hidden(string value) => value;
            }

            internal static class InternalTools
            {
                internal static string Normalize(string value) => "internal:" + value.Trim();
            }

            public sealed class Counter
            {
                public Counter(double initial) => Value = initial;
                public double Value { get; }
                public double Add(double amount) => Value + amount;
            }

            public static class Formatter
            {
                public static string Format(string value) => "string:" + value;
                public static string Format(double value) => "number:" + value;
            }

            #if FEATURE_X
            public static class OptionalApi
            {
                public static string Value() => "feature";
            }
            #endif
            """;
        fixture.Write("Demo/Names.cs", names);
        fixture.Write("Demo/Program.cs", """
            using Demo.Copeland;

            System.Console.WriteLine(Greeting.Message(" wyrm "));
            System.Console.WriteLine(Feature.Calculate(2, 3));
            System.Console.WriteLine(Feature.Internal(" wyrm "));
            System.Console.WriteLine(Feature.Format("wyrm"));
            System.Console.WriteLine(Feature.Optional());
            System.Console.WriteLine(Feature.Inline(" wyrm "));
            """);
        fixture.Write("Demo/Greeting.ts", """
            using Demo;

            function Message(name: string): string {
                return Names.Normalize(name);
            }
            """);
        fixture.Write("Demo/Feature.ts", """
            using Demo;

            function Calculate(initial: number, amount: number): number {
                const counter: Counter = new Counter(initial);
                return counter.Add(amount) + counter.Value;
            }

            function Internal(value: string): string {
                return InternalTools.Normalize(value);
            }

            function Format(value: string): string {
                return Formatter.Format(value);
            }

            function Optional(): string {
                return OptionalApi.Value();
            }

            function Inline(value: string): string {
                csharp {
                    return Names.Normalize(value);
                }
            }
            """);

        fixture.Run("Demo", "restore");
        fixture.Run("Demo", "build", "--no-restore");
        fixture.Run("Demo", "run", "--no-build").AssertOutput("WYRM", "7", "internal:wyrm", "string:wyrm", "feature", "WYRM");
        fixture.Run("Demo", "publish", "--no-restore", "-o", "publish");

        string generated = Path.Combine(fixture.Root, "Demo", "obj", "Debug", "net10.0", "Copeland", "Greeting.g.cs");
        Assert.Contains("global::Demo.Names.Normalize", File.ReadAllText(generated), StringComparison.Ordinal);
        string featureGenerated = Path.Combine(fixture.Root, "Demo", "obj", "Debug", "net10.0", "Copeland", "Feature.g.cs");
        Assert.Contains("return Names.Normalize(value);", File.ReadAllText(featureGenerated), StringComparison.Ordinal);

        fixture.Write("Demo/Names.cs", names.Replace("public static string Normalize", "public static string Renamed", StringComparison.Ordinal));
        ProcessResult renamed = fixture.RunExpectingFailure("Demo", "build", "--no-restore");
        Assert.Contains("Greeting.ts", renamed.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COPE-CLR", renamed.Output, StringComparison.Ordinal);

        fixture.Write("Demo/Greeting.ts", """
            using Demo;
            function Message(name: string): string { return Names.Hidden(name); }
            """);
        ProcessResult result = fixture.RunExpectingFailure("Demo", "build", "--no-restore");
        Assert.Contains("Greeting.ts", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COPE-CLR-0004", result.Output, StringComparison.Ordinal);

        fixture.Write("Demo/Greeting.ts", """
            using Demo;
            function Message(name: string): string {
                csharp { return Names.DoesNotExist(name); }
            }
            """);
        ProcessResult csharpError = fixture.RunExpectingFailure("Demo", "build", "--no-restore");
        Assert.Contains("Greeting.ts", csharpError.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CS0117", csharpError.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Relative_modules_emit_one_graph_artifact_with_internal_private_functions()
    {
        using var fixture = new TemporaryProject();
        string taskAssembly = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.MSBuild.dll"));
        string targets = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.Sdk.targets"));
        fixture.Write("Demo/Demo.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Demo</RootNamespace>
                <CopelandTaskAssembly>{{taskAssembly}}</CopelandTaskAssembly>
              </PropertyGroup>
              <ItemGroup>
                <CopelandCompile Include="Library.ts" />
                <CopelandCompile Include="Main.ts" />
              </ItemGroup>
              <Import Project="{{targets}}" />
            </Project>
            """);
        fixture.Write("Demo/Library.ts", """
            function Normalize(value: string): string { return value; }
            export function Public(value: string): string { return Normalize(value); }
            """);
        fixture.Write("Demo/Main.ts", """
            import { Public } from "./Library";
            export function Run(): string { return Public("module"); }
            """);
        fixture.Write("Demo/Program.cs", """
            using Demo.Copeland;
            System.Console.WriteLine(Main.Run());
            """);

        fixture.Run("Demo", "restore");
        fixture.Run("Demo", "build", "--no-restore");
        fixture.Run("Demo", "run", "--no-build").AssertOutput("module");

        string generated = Path.Combine(fixture.Root, "Demo", "obj", "Debug", "net10.0", "Copeland", "CopelandProject.g.cs");
        string source = File.ReadAllText(generated);
        Assert.Contains("internal static string Normalize", source, StringComparison.Ordinal);
        Assert.Contains("public static string Run", source, StringComparison.Ordinal);
    }

    private static string CreateProjectFile(bool includeFeature = true, bool includeProjectReference = true, bool includePackage = true)
    {
        string taskAssembly = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.MSBuild.dll"));
        string targets = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.Sdk.targets"));
        string featureItem = includeFeature ? "<CopelandCompile Include=\"Feature.ts\" />" : string.Empty;
        string packageItem = includePackage ? "<CopelandCompile Include=\"Package.ts\" />" : string.Empty;
        string projectReference = includeProjectReference ? "<ProjectReference Include=\"..\\Library\\Library.csproj\" />" : string.Empty;
        string packageReference = includePackage ? "<PackageReference Include=\"Tomlyn\" Version=\"2.9.0\" />" : string.Empty;
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Demo</RootNamespace>
                <CopelandTaskAssembly>{{taskAssembly}}</CopelandTaskAssembly>
              </PropertyGroup>
              <ItemGroup>
                {{projectReference}}
                {{packageReference}}
                <CopelandCompile Include="Greeting.ts" />
                {{featureItem}}
                {{packageItem}}
              </ItemGroup>
              <Import Project="{{targets}}" />
            </Project>
            """;
    }

    private static string CreateMixedProjectFile()
    {
        string taskAssembly = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.MSBuild.dll"));
        string targets = EscapeXml(Path.Combine(AppContext.BaseDirectory, "Copeland.TS.Sdk.targets"));
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Demo</RootNamespace>
                <DefineConstants>FEATURE_X</DefineConstants>
                <CopelandTaskAssembly>{{taskAssembly}}</CopelandTaskAssembly>
              </PropertyGroup>
              <ItemGroup>
                <CopelandCompile Include="Greeting.ts" />
                <CopelandCompile Include="Feature.ts" />
              </ItemGroup>
              <Import Project="{{targets}}" />
            </Project>
            """;
    }

    private static string EscapeXml(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("'", "&apos;", StringComparison.Ordinal);

    private sealed class TemporaryProject : IDisposable
    {
        public TemporaryProject()
        {
            Root = Path.Combine(Path.GetTempPath(), "Copeland-MSBuild-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Write(string relativePath, string content)
        {
            string path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Delete(string relativePath) => File.Delete(Path.Combine(Root, relativePath));

        public ProcessResult Run(string workingDirectory, params string[] arguments)
        {
            ProcessResult result = RunCore(workingDirectory, arguments);
            Assert.True(result.ExitCode == 0, result.Output);
            return result;
        }

        public ProcessResult RunWithNugetPackages(string workingDirectory, string packageCache, params string[] arguments)
        {
            ProcessResult result = RunCore(workingDirectory, arguments, packageCache);
            Assert.True(result.ExitCode == 0, result.Output);
            return result;
        }

        public ProcessResult RunExpectingFailure(string workingDirectory, params string[] arguments)
        {
            ProcessResult result = RunCore(workingDirectory, arguments);
            Assert.NotEqual(0, result.ExitCode);
            return result;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private ProcessResult RunCore(string workingDirectory, IReadOnlyList<string> arguments, string? packageCache = null)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.Combine(Root, workingDirectory),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (packageCache is not null)
            {
                startInfo.Environment["NUGET_PACKAGES"] = packageCache;
            }

            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, output);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output)
    {
        public ProcessResult AssertOutput(params string[] values)
        {
            foreach (string value in values)
            {
                Assert.Contains(value, Output, StringComparison.Ordinal);
            }

            return this;
        }
    }
}
