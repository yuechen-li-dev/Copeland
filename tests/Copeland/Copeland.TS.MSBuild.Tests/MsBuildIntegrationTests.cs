using System.Diagnostics;
using Xunit;

namespace Copeland.TS.MSBuild.Tests;

public sealed class MsBuildIntegrationTests
{
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

        private ProcessResult RunCore(string workingDirectory, IReadOnlyList<string> arguments)
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
