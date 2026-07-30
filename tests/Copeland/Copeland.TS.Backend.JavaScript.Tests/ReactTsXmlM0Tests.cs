using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class ReactTsXmlM0Tests
{
    [Fact]
    public void Plain_text_call_uses_the_canonical_document_renderer()
    {
        var project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource(
                "Plain.tsx",
                "Plain.tsx",
                """
                import { createElement } from "react";
                export function Label(): ReactNode { return Text("<safe> & **strong**"); }
                """),
        ],
        new CopelandCompilationOptions
        {
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
            NpmPackages =
            [
                new CopelandNpmPackageContract("react", "19.2.7", [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
            ],
        });

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics.Select(diagnostic => diagnostic.Message)));
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!);
        string output = emitted.Files["Plain.js"];
        Assert.Contains("createElement(\"div\"", output, StringComparison.Ordinal);
        Assert.Contains("createElement(\"p\"", output, StringComparison.Ordinal);
        Assert.Contains("createElement(\"strong\"", output, StringComparison.Ordinal);
        Assert.Contains("<safe> &", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_document_binding_renders_semantic_react_elements_without_source_inline_parsing()
    {
        var project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource(
                "Document.tsx",
                "Document.tsx",
                """
                import { createElement } from "react";
                export function DocumentView(): ReactNode {
                    return <Document className="document-shell"><Heading className="text-fit-target" role="HeroHeading">Build **real software** with `Copeland` and [docs](#docs).</Heading><List><Item><Paragraph>First</Paragraph></Item></List><CodeBlock>const x = 1;</CodeBlock></Document>;
                }
                """),
        ],
        new CopelandCompilationOptions
        {
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
            NpmPackages =
            [
                new CopelandNpmPackageContract("react", "19.2.7", [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
            ],
        });

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics.Select(diagnostic => diagnostic.Message)));
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(project.MirProjectGraph!);
        string output = emitted.Files["Document.js"];
        Assert.Contains("createElement(\"h1\", { className: \"text-fit-target\"", output, StringComparison.Ordinal);
        Assert.Contains("createElement(\"strong\"", output, StringComparison.Ordinal);
        Assert.Contains("createElement(\"code\"", output, StringComparison.Ordinal);
        Assert.Contains("href: \"#docs\"", output, StringComparison.Ordinal);
        Assert.Contains("createElement(\"ul\"", output, StringComparison.Ordinal);
    }

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

    [Fact]
    public void React_profile_preserves_direct_clr_static_calls()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource(
                "Bridge.ts",
                "Bridge.ts",
                """
                using System.Text.Json;
                export record Request { message: string; count: int; }
                export function Serialize(request: Request): string {
                    return JsonSerializer.Serialize(request);
                }
                """),
        ],
        new CopelandCompilationOptions
        {
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
            ClrReferences = [new CopelandClrReference(typeof(System.Text.Json.JsonSerializer).Assembly.Location)],
        });

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("System.Text.Json.JsonSerializer.Serialize", project.Compilation!.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_react_profile_can_await_a_declared_remote_operation()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource(
                "Bridge.ts",
                "Bridge.ts",
                """
                export record Request { message: string; count: int; }
                export record BridgeError { kind: string; message: string; }
                export remote function SerializeState(request: Request): string ! BridgeError {
                    return request.message;
                }
                """),
            new CopelandProjectSource(
                "Main.ts",
                "Main.ts",
                """
                import { SerializeState, BridgeError, Request } from "./Bridge";
                async function Load(request: Request): string ! BridgeError {
                    const serialized: string = request.message;
                    const pending: Async<string ! BridgeError> = SerializeState({ message: serialized, count: request.count });
                    return await pending;
                }
                export function Main(): void { Load({ message: "ok", count: 0 }); }
                """),
        ],
        new CopelandCompilationOptions
        {
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
        });

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics.Select(diagnostic => diagnostic.Message)));
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(
            project.MirProjectGraph!,
            new JavaScriptEmissionOptions
            {
                RuntimeTarget = JavaScriptRuntimeTarget.Browser,
                RemoteOperationRoutes = new Dictionary<string, string>
                {
                    ["SerializeState"] = "/serialize-state",
                },
            });

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("SerializeState", emitted.Files["Main.js"], StringComparison.Ordinal);
        Assert.Contains("frame.__parameter_request", emitted.Files["Main.js"], StringComparison.Ordinal);
        Assert.Contains("frame.__local_serialized", emitted.Files["Main.js"], StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_effect_projects_declared_bridge_failure_to_a_typed_event()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource(
                "Bridge.ts",
                "Bridge.ts",
                """
                export record Request { message: string; count: int; }
                export record BridgeError { kind: string; message: string; }
                export remote function SerializeState(request: Request): string ! BridgeError {
                    return request.message;
                }
                """),
            new CopelandProjectSource(
                "Main.ts",
                "Main.ts",
                """
                import { SerializeState, BridgeError, Request } from "./Bridge";

                export enum AppEvent {
                    SerializationCompleted(serialized: string),
                    SerializationFailed(message: string),
                }

                async function SerializeEffect(send: (event: AppEvent) => void): void {
                    try {
                        const pending: Async<string ! BridgeError> = SerializeState({ message: "ok", count: 0 });
                        const serialized: string = await pending?;
                        send(AppEvent.SerializationCompleted(serialized))
                    } except (error) {
                        send(AppEvent.SerializationFailed("The CLR bridge request failed."))
                    };
                }

                export function Main(): void {
                    const send: (event: AppEvent) => void = (event: AppEvent) => {};
                    SerializeEffect(send);
                }
                """),
        ],
        new CopelandCompilationOptions
        {
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
        });

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics.Select(diagnostic => diagnostic.Message)));
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(
            project.MirProjectGraph!,
            new JavaScriptEmissionOptions
            {
                RuntimeTarget = JavaScriptRuntimeTarget.Browser,
                RemoteOperationRoutes = new Dictionary<string, string>
                {
                    ["SerializeState"] = "/serialize-state",
                },
            });

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string output = emitted.Files["Main.js"];
        Assert.Contains("SerializationFailed", output, StringComparison.Ordinal);
        Assert.Contains("The CLR bridge request failed.", output, StringComparison.Ordinal);
    }
}
