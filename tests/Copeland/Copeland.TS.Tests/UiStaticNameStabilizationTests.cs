using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class UiStaticNameStabilizationTests
{
    [Fact]
    public void Imported_enum_constructor_needs_no_runtime_capture()
    {
        CopelandProjectCompilation compilation = Compile(
            "export enum Event { Choose(id: int) }",
            """
            import { Event } from "./Events";
            export function Main(send: (event: Event) => void): void {
                const callback: () => void = capture { send } () => send(Event.Choose(1));
                callback();
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public void Real_enum_values_still_require_explicit_capture()
    {
        CopelandProjectCompilation compilation = Compile(
            "export enum Event { Choose(id: int) }",
            """
            import { Event } from "./Events";
            export function Main(send: (event: Event) => void): void {
                const selected: Event = Event.Choose(1);
                const callback: () => void = capture { send } () => send(selected);
            }
            """);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-CALL-0017"
            && diagnostic.Message.Contains("selected", StringComparison.Ordinal));
    }

    [Fact]
    public void Parameter_shadowing_a_static_enum_name_still_requires_capture()
    {
        CopelandProjectCompilation compilation = Compile(
            "export enum Event { Choose(id: int) }",
            """
            import { Event } from "./Events";
            export function Main(Event: int): int {
                const callback: () => int = () => Event + 1;
                return callback();
            }
            """);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-CALL-0017");
    }

    [Fact]
    public void Enum_cases_and_match_patterns_do_not_reference_a_foreign_function_with_the_same_name()
    {
        CopelandProjectCompilation compilation = Compile(
            "function Choose(id: int): int { return id; }",
            """
            enum Event { Choose(id: int) }
            export function Main(event: Event): int {
                return switch event { Choose(id) => id };
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public void Real_function_calls_inside_match_arms_still_require_imports()
    {
        CopelandProjectCompilation compilation = Compile(
            "function Choose(id: int): int { return id; }",
            """
            enum Event { Choose(id: int) }
            export function Main(event: Event): int {
                return switch event { Choose(id) => Choose(id) };
            }
            """);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-MODULE-0007");
    }

    private static CopelandProjectCompilation Compile(string events, string main)
    {
        return CopelandProjectCompiler.CompileToMir(
            [
                new CopelandProjectSource("Events.ts", "Events.ts", events),
                new CopelandProjectSource("Main.ts", "Main.ts", main)
            ], new CopelandCompilationOptions());
    }
}
