using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class ReactThirdPartyComponentM1Tests
{
    [Fact]
    public void Compound_component_identity_props_children_and_bool_callback_survive_mir_and_emission()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource(
                "App.tsx",
                "App.tsx",
                """
                import { createElement } from "react";
                import { Dialog } from "@base-ui-components/react/dialog";

                export function View(): ReactNode {
                    return <main>
                        <Dialog.Root open={true} onOpenChange={open => {}}>
                            <Dialog.Portal>
                                <Dialog.Backdrop />
                                <Dialog.Popup>
                                    <Dialog.Title>Third-party React works</Dialog.Title>
                                    <Dialog.Description>Base UI is running inside Copeland TS.</Dialog.Description>
                                    <Dialog.Close>Close</Dialog.Close>
                                </Dialog.Popup>
                            </Dialog.Portal>
                        </Dialog.Root>
                    </main>;
                }
                """),
        ],
        new CopelandCompilationOptions
        {
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
            NpmPackages =
            [
                new CopelandNpmPackageContract("react", "19.2.7", [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
                new CopelandNpmPackageContract(
                    "@base-ui-components/react/dialog",
                    "1.0.0-rc.0",
                    [],
                    Components:
                    [
                        new CopelandNpmComponentContract(
                            "Dialog",
                            Members:
                            [
                                Component("Root", ("open", "boolean", false), ("onOpenChange", "(boolean)=>void", false), ("children", "ReactNode", false)),
                                Component("Portal", ("children", "ReactNode", false)),
                                Component("Backdrop"),
                                Component("Popup", ("children", "ReactNode", false)),
                                Component("Title", ("children", "ReactNode", false)),
                                Component("Description", ("children", "ReactNode", false)),
                                Component("Close", ("children", "ReactNode", false)),
                            ]),
                    ]),
            ],
        });

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("npm:@base-ui-components/react/dialog@1.0.0-rc.0:Dialog import Dialog", project.Compilation!.MirText, StringComparison.Ordinal);

        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(
            project.MirProjectGraph!,
            new JavaScriptEmissionOptions
            {
                RuntimeTarget = JavaScriptRuntimeTarget.Browser,
                Profile = JavaScriptEmissionProfile.Production,
            });

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string output = emitted.Files["App.js"];
        Assert.Contains("import { Dialog } from \"@base-ui-components/react/dialog\";", output, StringComparison.Ordinal);
        Assert.Contains("createElement(Dialog.Root, { onOpenChange:", output, StringComparison.Ordinal);
        Assert.Contains("open: true", output, StringComparison.Ordinal);
        Assert.Contains("Third-party React works", output, StringComparison.Ordinal);
        Assert.Contains("expected boolean callback argument", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_component_property_is_diagnostic()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("App.tsx", "App.tsx", """
                import { createElement } from "react";
                import { Dialog } from "@base-ui-components/react/dialog";
                export function View(): ReactNode { return <Dialog.Root unknown={"bad"} />; }
                """),
        ],
        new CopelandCompilationOptions
        {
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
            NpmPackages =
            [
                new CopelandNpmPackageContract("react", "19.2.7", [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
                new CopelandNpmPackageContract(
                    "@base-ui-components/react/dialog",
                    "1.0.0-rc.0",
                    [],
                    Components: [new CopelandNpmComponentContract("Dialog", Members: [Component("Root")])]),
            ],
        });

        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-REACT-0013");
    }

    [Fact]
    public void Incompatible_component_callback_is_diagnostic()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("App.tsx", "App.tsx", """
                import { createElement } from "react";
                import { Dialog } from "@base-ui-components/react/dialog";
                function InvalidCallback(open: string): void {}
                export function View(): ReactNode { return <Dialog.Root onOpenChange={InvalidCallback} />; }
                """),
        ],
        new CopelandCompilationOptions
        {
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
            NpmPackages =
            [
                new CopelandNpmPackageContract("react", "19.2.7", [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
                new CopelandNpmPackageContract(
                    "@base-ui-components/react/dialog",
                    "1.0.0-rc.0",
                    [],
                    Components: [new CopelandNpmComponentContract("Dialog", Members: [Component("Root", ("onOpenChange", "(boolean)=>void", false))])]),
            ],
        });

        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-REACT-0014");
    }

    private static CopelandNpmComponentMemberContract Component(
        string name,
        params (string Name, string Type, bool Required)[] properties)
        => new(name, properties.Select(property => new CopelandNpmComponentPropertyContract(property.Name, property.Type, property.Required)).ToArray());
}
