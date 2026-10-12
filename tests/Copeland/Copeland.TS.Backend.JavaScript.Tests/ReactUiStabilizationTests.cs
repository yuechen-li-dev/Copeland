using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class ReactUiStabilizationTests
{
    [Fact]
    public void Local_projection_preserves_spacing_style_and_overflow_on_a_single_content_host()
    {
        JavaScriptProjectCompilation emitted = Emit("""
            import { createElement } from "react";
            export function Main(): ReactNode {
                stream Surface<0px, 0px> {
                    width: fill;
                    height: fill;
                    gap: 12px;
                    padding: 16px;
                    style: { fill: "#123456" };
                    overflow: scrollY;
                    content: <p>Long readable content</p> { height: fill; }
                }
                return Surface();
            }
            """);

        string css = emitted.Files["generated/layouts.css"];
        Assert.DoesNotContain("display: contents", css, StringComparison.Ordinal);
        Assert.Contains("gap: 12px;", css, StringComparison.Ordinal);
        Assert.Contains("padding: 16px 16px 16px 16px;", css, StringComparison.Ordinal);
        Assert.Contains("background: #123456;", css, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("row", "width", "300px")]
    [InlineData("column", "height", "64px")]
    public void Fixed_stack_tracks_do_not_shrink_and_fill_uses_the_remaining_axis(string kind, string axis, string size)
    {
        JavaScriptProjectCompilation emitted = Emit($$"""
            import { createElement } from "react";
            export function Main(): ReactNode {
                stream Surface<0px, 0px> {
                    width: fill;
                    height: 600px;
                    {{kind}} root {
                        first: <span>Fixed</span> { {{axis}}: {{size}}; }
                        second: <span>Remaining</span> { {{axis}}: fill; }
                    }
                }
                return Surface();
            }
            """);

        string css = emitted.Files["generated/layouts.css"];
        Assert.Contains("flex: 0 0 " + size + ";", css, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 0px;", css, StringComparison.Ordinal);
        Assert.Contains(axis + ": auto;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_spaces_survive_interpolation_and_inline_element_boundaries()
    {
        JavaScriptProjectCompilation emitted = Emit("""
            import { createElement } from "react";
            export function Main(name: string): ReactNode {
                return <p>Before {name} after <strong>bold</strong> <span>end</span>.</p>;
            }
            """);

        string javascript = emitted.Files["Main.js"];
        Assert.Contains("\"Before \", name, \" after \"", javascript, StringComparison.Ordinal);
        Assert.Contains("\"bold\"), \" \",", javascript, StringComparison.Ordinal);
    }

    [Fact]
    public void Formatting_newlines_do_not_introduce_indentation_into_react_text()
    {
        JavaScriptProjectCompilation emitted = Emit("""
            import { createElement } from "react";
            export function Main(): ReactNode {
                return <p>
                    A useful
                    explanation.
                </p>;
            }
            """);

        Assert.Contains("\"A useful explanation.\"", emitted.Files["Main.js"], StringComparison.Ordinal);
    }

    [Fact]
    public void Numeric_keys_and_focus_order_remain_typed_integer_props()
    {
        JavaScriptProjectCompilation emitted = Emit("""
            import { createElement } from "react";
            export function Main(): ReactNode { return <span key={1} tabIndex={0}>Focus</span>; }
            """);

        Assert.Contains("key: 1", emitted.Files["Main.js"], StringComparison.Ordinal);
        Assert.Contains("tabIndex: 0", emitted.Files["Main.js"], StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_native_input_events_have_one_precise_diagnostic()
    {
        CopelandProjectCompilation compilation = Compile("""
            import { createElement } from "react";
            export function Main(): ReactNode {
                return <input value="" onChange={(value: string) => {}} />;
            }
            """);

        var diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("COPE-REACT-0017", diagnostic.Id);
        Assert.Contains("onValueChange", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("unsupported-capability", diagnostic.RepairKind);
        Assert.NotNull(diagnostic.SuggestedReplacement);
    }

    [Fact]
    public void Invalid_root_dimensions_are_reported_once_per_authored_property()
    {
        CopelandProjectCompilation compilation = Compile("""
            import { createElement } from "react";
            export function Main(width: int): ReactNode {
                stream Surface<0px, 0px> {
                    width: width;
                    height: fit;
                    content: <span>Content</span> { height: fit; }
                }
                return Surface();
            }
            """);

        var diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("COPE-LAYOUT-DIMENSION-0001", diagnostic.Id);
    }

    [Fact]
    public void Unsupported_relative_style_units_report_failure_without_publishing_partial_css()
    {
        CopelandProjectCompilation compilation = Compile("""
            import { createElement } from "react";
            export function Main(): ReactNode {
                stream Surface<0px, 0px> {
                    width: fill;
                    height: fit;
                    style: { border: { width: 0.5ui, color: "#123456", style: "solid" } };
                    content: <p>Content</p> { height: fit; }
                }
                return Surface();
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!);
        emitted = LayoutJavaScriptProjectEmitter.AddLayouts(emitted, compilation.Modules);
        Assert.False(emitted.Success);
        Assert.Equal("COPE-MACHINA-STYLE-0001", Assert.Single(emitted.Diagnostics).Id);
        Assert.DoesNotContain("generated/layouts.css", emitted.Files.Keys);
    }

    private static JavaScriptProjectCompilation Emit(string source)
    {
        CopelandProjectCompilation compilation = Compile(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!);
        emitted = LayoutJavaScriptProjectEmitter.AddLayouts(emitted, compilation.Modules);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return emitted;
    }

    private static CopelandProjectCompilation Compile(string source)
    {
        return CopelandProjectCompiler.CompileToMir(
            [new CopelandProjectSource("Main.tsx", "Main.tsx", source)],
            new CopelandCompilationOptions
            {
                TsXmlProfile = CopelandTsXmlProfile.ReactM0,
                NpmPackages =
                [
                    new CopelandNpmPackageContract("react", "19.2.7",
                        [new CopelandNpmFunctionContract("createElement", [], "ReactNode")])
                ]
            });
    }
}
