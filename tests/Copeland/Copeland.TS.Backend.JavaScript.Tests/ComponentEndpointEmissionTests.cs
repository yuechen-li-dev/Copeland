using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class ComponentEndpointEmissionTests
{
    [Theory]
    [InlineData(JavaScriptEmissionProfile.Diagnostic)]
    [InlineData(JavaScriptEmissionProfile.Symbolic)]
    [InlineData(JavaScriptEmissionProfile.Production)]
    public void Every_browser_profile_checks_typed_component_inputs(JavaScriptEmissionProfile profile)
    {
        CopelandProjectCompilation project = Compile();
        Assert.True(project.Success, string.Join("\n", project.Diagnostics.Select(diagnostic => diagnostic.Message)));
        JavaScriptProjectCompilation emission = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!,
            new JavaScriptEmissionOptions { RuntimeTarget = JavaScriptRuntimeTarget.Browser, Profile = profile });
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string javascript = emission.Files["Main.js"];
        Assert.Contains("Number.isInteger(", javascript);
        Assert.Contains("Array.from(", javascript);
        Assert.Contains("argument tuple of the declared arity", javascript);
        Assert.Contains("registerComponentFrames", javascript);
        Assert.Contains("Component input", javascript);
    }

    [Fact]
    public void Node_emission_has_an_explicit_adapter_diagnostic()
    {
        CopelandProjectCompilation project = Compile();
        JavaScriptProjectCompilation emission = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!);
        Assert.Contains(emission.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-COMPONENT-ON-0001");
    }

    private static CopelandProjectCompilation Compile()
    {
        return CopelandProjectCompiler.CompileToMir(
            [new CopelandProjectSource("Main.tsx", "Main.tsx", """
                import { createElement } from "react";
                export function Counter(): ReactNode {
                    state: int = 0;
                    on Add(values: int[]) => state + values[0];
                    return <button onClick={capture { send: on.Add } () => send([1])}>{state}</button>;
                }
                """)],
            new CopelandCompilationOptions
            {
                TsXmlProfile = CopelandTsXmlProfile.ReactM0,
                NpmPackages = [new CopelandNpmPackageContract("react", "19.2.7",
                    [new CopelandNpmFunctionContract("createElement", [], "ReactNode")])],
            });
    }
}
