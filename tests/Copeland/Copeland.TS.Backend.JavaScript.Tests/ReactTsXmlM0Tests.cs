using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class ReactTsXmlM0Tests
{
    [Fact]
    public void Explicit_react_profile_lowers_bounded_tsxml_to_imported_createElement_and_root_render()
    {
        var project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource(
                "Main.tsx",
                "Main.tsx",
                """
                import { createElement } from "react";
                import { createRoot } from "react-dom/client";
                import { getMountElement } from "@copeland/browser-v1";

                export function View(): ReactNode {
                    return <main><h1>Copeland TS + React</h1><p>Count: {0}</p><button onClick={() => {}}>Increment</button></main>;
                }

                export function Main(): void {
                    const root: ReactRoot = createRoot(getMountElement("app"));
                    root.render(View());
                }
                """),
        ],
        new CopelandCompilationOptions
        {
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
            NpmPackages =
            [
                new CopelandNpmPackageContract("react", "19.2.7", [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
                new CopelandNpmPackageContract("react-dom/client", "19.2.7", [new CopelandNpmFunctionContract("createRoot", ["ReactMountElement"], "ReactRoot")]),
            ],
            JavaScriptHostModules =
            [
                new CopelandJavaScriptHostModuleContract(
                    "@copeland/browser-v1",
                    [new CopelandJavaScriptHostFunctionContract("getMountElement", [CopelandJavaScriptHostType.String], new CopelandJavaScriptHostType.Named("ReactMountElement"))]),
            ],
        });

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics.Select(diagnostic => diagnostic.Message)));
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(
            project.MirProjectGraph!,
            new JavaScriptEmissionOptions
            {
                RuntimeTarget = JavaScriptRuntimeTarget.Browser,
                Profile = JavaScriptEmissionProfile.Production,
            });

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string output = emitted.Files["Main.js"];
        Assert.Contains("import { createElement } from \"react\";", output, StringComparison.Ordinal);
        Assert.Contains("import { createRoot } from \"react-dom/client\";", output, StringComparison.Ordinal);
        Assert.Contains("createElement(\"button\", { onClick:", output, StringComparison.Ordinal);
        Assert.Contains("root.render(View())", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Tsxml_remains_unprofiled_without_explicit_react_selection()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            "function View(): void { <main />; }",
            new CopelandCompilationOptions { SourcePath = "View.tsx" });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSXML-0101");
    }
}
